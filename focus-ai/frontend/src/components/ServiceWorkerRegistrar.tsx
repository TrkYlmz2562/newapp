'use client';

import { useEffect } from 'react';

/**
 * Registers the service worker that makes the app installable and readable
 * offline. Deliberately a no-op in development, where a stale cached bundle is
 * far more trouble than offline support is worth.
 *
 * IMPORTANT, and easy to miss: `'serviceWorker' in navigator` is false outside a
 * secure context. Reaching the app over plain HTTP on a LAN or Tailscale address
 * — http://100.x.y.z:3000 — therefore disables offline reading, installation and
 * push entirely, silently, with the app otherwise looking completely healthy.
 * localhost is exempt, which is why this never shows up in development.
 *
 * The fix is transport, not code: serve the app over HTTPS. See the Tailscale
 * section in the README.
 */
export function ServiceWorkerRegistrar() {
  useEffect(() => {
    if (process.env.NODE_ENV !== 'production') return;
    if (typeof navigator === 'undefined') return;

    if (!('serviceWorker' in navigator)) {
      if (!window.isSecureContext) {
        // Said out loud rather than swallowed: a reader wondering why "kaydet"
        // does not survive a tunnel ride deserves to find the reason.
        console.warn(
          '[Focus AI] Çevrimdışı okuma ve bildirimler kapalı: sayfa güvenli bağlamda değil. ' +
            'HTTPS üzerinden açılması gerekiyor (README → Tailscale).',
        );
      }
      return;
    }

    const register = () => {
      navigator.serviceWorker.register('/sw.js').catch(() => {
        // Registration failures are non-fatal — the app works fine online.
      });
    };

    // Wait for load so the worker never competes with first paint.
    if (document.readyState === 'complete') {
      register();
    } else {
      window.addEventListener('load', register, { once: true });
    }
  }, []);

  return null;
}
