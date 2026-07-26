import { API_BASE } from './api';

/**
 * Keeps saved stories readable with no signal.
 *
 * Bookmarking is the trigger, deliberately: "kaydet" already means "I want this
 * later", and later is exactly when there is no signal — on a metro, on a plane.
 * A second button asking the same question in different words would be noise.
 *
 * Everything here is best-effort and silent. Offline saving is a bonus on top of
 * a bookmark, never a reason for the bookmark itself to appear to fail, so no
 * function in this file throws or reports.
 */

/** The two requests a story detail page makes: its own HTML, and its API payload. */
function urlsFor(slug: string): string[] {
  const encoded = encodeURIComponent(slug);

  return [
    new URL(`/story/${encoded}`, window.location.origin).toString(),
    // API_BASE is '' when the API is served from the same origin (the Tailscale
    // HTTPS setup), and an absolute URL otherwise. URL() resolves both.
    new URL(`${API_BASE}/api/stories/${encoded}`, window.location.origin).toString(),
  ];
}

async function post(type: string, slug: string): Promise<void> {
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) {
    // No secure context, no service worker, no offline cache. Nothing to do —
    // and nothing to complain about, since the app works fine online.
    return;
  }

  try {
    // `ready` rather than `controller`: on the very first load the worker is
    // registered but not yet controlling the page, and the message would be
    // dropped silently.
    const registration = await navigator.serviceWorker.ready;
    registration.active?.postMessage({ type, urls: urlsFor(slug) });
  } catch {
    // Registration can fail for reasons the page cannot fix.
  }
}

/** Fetches and stores a story so it can be opened with no connection. */
export const saveForOffline = (slug: string) => post('FOCUSAI_SAVE_OFFLINE', slug);

/** Drops a story from the offline cache. Paired with un-bookmarking. */
export const forgetOffline = (slug: string) => post('FOCUSAI_FORGET_OFFLINE', slug);
