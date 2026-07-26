/*
 * Focus AI service worker.
 *
 * Hand-written rather than generated: the caching rules here are product
 * decisions, not boilerplate. A news reader must never serve a stale digest as
 * if it were today's, but it must also stay readable on a metro with no signal.
 * The split below is what reconciles those.
 */

const VERSION = 'v2';
const SHELL_CACHE = `focusai-shell-${VERSION}`;
const CONTENT_CACHE = `focusai-content-${VERSION}`;

/**
 * Stories the reader deliberately saved. Kept apart from CONTENT_CACHE because
 * that one expires after twelve hours — correct for a feed, wrong for something
 * someone saved on purpose to read on a plane. Nothing here expires; entries
 * leave only when the reader un-saves the story.
 */
const OFFLINE_CACHE = `focusai-offline-${VERSION}`;

/** Navigations fall back to this when the network is unreachable. */
const OFFLINE_URL = '/offline.html';

const SHELL_ASSETS = [OFFLINE_URL, '/manifest.webmanifest'];

/** Cached API responses expire after this; past it, an empty state beats a lie. */
const CONTENT_MAX_AGE_MS = 12 * 60 * 60 * 1000;

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(SHELL_CACHE)
      .then((cache) => cache.addAll(SHELL_ASSETS))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(
          keys
            .filter((key) => key.startsWith('focusai-') && !key.endsWith(VERSION))
            .map((key) => caches.delete(key)),
        ),
      )
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  const { request } = event;

  if (request.method !== 'GET') return;

  const url = new URL(request.url);

  // Never cache authentication or mutations — a replayed token response would
  // be both wrong and a security problem.
  if (url.pathname.startsWith('/api/auth')) return;

  if (request.mode === 'navigate') {
    event.respondWith(networkFirstNavigation(request));
    return;
  }

  if (url.pathname.startsWith('/api/')) {
    event.respondWith(networkFirstApi(request));
    return;
  }

  // Build output is content-hashed, so cache-first is safe and fast.
  if (url.origin === self.location.origin && url.pathname.startsWith('/_next/static/')) {
    event.respondWith(cacheFirst(request, SHELL_CACHE));
  }
});

async function networkFirstNavigation(request) {
  try {
    return await fetch(request);
  } catch {
    // Deliberately saved pages are checked first: they are the ones the reader
    // expects to work with no signal, and they never expire.
    //
    // ignoreVary because the worker saved these with a plain fetch while the page
    // asks with an Authorization header. A Vary on the response would otherwise
    // make the entry unmatchable — saved, present, and never served.
    const saved = await caches.match(request, { cacheName: OFFLINE_CACHE, ignoreVary: true });
    if (saved) return saved;

    const cached = await caches.match(request);
    return cached ?? (await caches.match(OFFLINE_URL)) ?? Response.error();
  }
}

async function networkFirstApi(request) {
  const cache = await caches.open(CONTENT_CACHE);

  try {
    const response = await fetch(request);

    if (response.ok) {
      // Store a timestamped clone so staleness can be judged on read.
      const body = await response.clone().blob();
      const headers = new Headers(response.headers);
      headers.set('x-focusai-cached-at', String(Date.now()));
      await cache.put(request, new Response(body, { status: response.status, headers }));
    }

    return response;
  } catch (error) {
    const saved = await caches.match(request, { cacheName: OFFLINE_CACHE, ignoreVary: true });
    if (saved) return saved;

    const cached = await cache.match(request);
    if (!cached) throw error;

    const cachedAt = Number(cached.headers.get('x-focusai-cached-at') ?? 0);
    if (Date.now() - cachedAt > CONTENT_MAX_AGE_MS) {
      await cache.delete(request);
      throw error;
    }

    return cached;
  }
}

/*
 * Offline saving. The page asks for a story to be kept; the worker fetches both
 * halves of it — the rendered page and the API response the page reads — and
 * stores them where nothing expires them.
 *
 * Doing the fetch here rather than in the page is what makes it work: the entries
 * have to land in the worker's cache keyed by the exact Request the fetch handler
 * will later look up, and only the worker can guarantee that.
 */
self.addEventListener('message', (event) => {
  const data = event.data;
  if (!data || typeof data.type !== 'string') return;

  if (data.type === 'FOCUSAI_SAVE_OFFLINE') {
    event.waitUntil(saveOffline(data.urls ?? []));
  } else if (data.type === 'FOCUSAI_FORGET_OFFLINE') {
    event.waitUntil(forgetOffline(data.urls ?? []));
  }
});

async function saveOffline(urls) {
  const cache = await caches.open(OFFLINE_CACHE);

  await Promise.all(
    urls.map(async (url) => {
      try {
        // credentials:'include' would attach cookies we do not use; the story
        // detail endpoint is readable anonymously, and caching a personalised
        // response would leak one reader's state into another's cache.
        const response = await fetch(url, { cache: 'no-store' });
        if (response.ok) await cache.put(url, response);
      } catch {
        // Saving is best-effort: no signal now simply means nothing to save.
      }
    }),
  );
}

async function forgetOffline(urls) {
  const cache = await caches.open(OFFLINE_CACHE);
  await Promise.all(urls.map((url) => cache.delete(url)));
}

async function cacheFirst(request, cacheName) {
  const cached = await caches.match(request);
  if (cached) return cached;

  const response = await fetch(request);
  if (response.ok) {
    const cache = await caches.open(cacheName);
    await cache.put(request, response.clone());
  }

  return response;
}

/*
 * Web Push. The backend currently logs instead of sending (see
 * LoggingPushNotifier), so this handler is inert until a VAPID sender is wired
 * up — but the client side is complete and correct, so enabling push is a
 * server-side change only.
 */
self.addEventListener('push', (event) => {
  if (!event.data) return;

  let payload;
  try {
    payload = event.data.json();
  } catch {
    payload = { title: 'Focus AI', body: event.data.text() };
  }

  event.waitUntil(
    self.registration.showNotification(payload.title ?? 'Focus AI', {
      body: payload.body ?? '',
      icon: '/icons/icon-192.svg',
      badge: '/icons/icon-192.svg',
      tag: payload.tag ?? 'focusai-digest',
      data: { url: payload.url ?? '/' },
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const target = event.notification.data?.url ?? '/';

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      // Reuse an open tab rather than piling up windows.
      for (const client of clientList) {
        if ('focus' in client) {
          client.navigate(target);
          return client.focus();
        }
      }
      return self.clients.openWindow(target);
    }),
  );
});
