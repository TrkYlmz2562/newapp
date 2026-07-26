'use client';

import { useEffect } from 'react';

/**
 * Registers the service worker that makes the app installable and readable
 * offline. Deliberately a no-op in development, where a stale cached bundle is
 * far more trouble than offline support is worth.
 */
export function ServiceWorkerRegistrar() {
  useEffect(() => {
    if (process.env.NODE_ENV !== 'production') return;
    if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return;

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
