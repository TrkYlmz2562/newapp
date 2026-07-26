'use client';

import { useEffect, useState } from 'react';

/**
 * Says so when the device is offline.
 *
 * Without it the app is quietly ambiguous: cached cards render exactly like live
 * ones, so a reader cannot tell whether they are looking at the current feed or
 * at whatever was in the cache when the signal dropped. For a news product that
 * distinction is the whole point — this is the same reason the service worker
 * expires cached feed responses after twelve hours rather than serving them
 * forever.
 */
export function OfflineBanner() {
  // Starts optimistic: navigator.onLine is unavailable during server rendering,
  // and flashing an offline warning on a healthy connection is worse than being
  // a frame late to show it.
  const [offline, setOffline] = useState(false);

  useEffect(() => {
    const sync = () => setOffline(!navigator.onLine);

    sync();
    window.addEventListener('online', sync);
    window.addEventListener('offline', sync);

    return () => {
      window.removeEventListener('online', sync);
      window.removeEventListener('offline', sync);
    };
  }, []);

  if (!offline) return null;

  return (
    <div
      role="status"
      className="sticky top-0 z-50 flex items-center justify-center gap-2 bg-signal-caution/15
                 px-4 py-1.5 text-center font-mono text-[11px] text-signal-caution
                 backdrop-blur"
    >
      <span className="h-1.5 w-1.5 flex-none rounded-full bg-signal-caution" aria-hidden="true" />
      Bağlantı yok — kaydettiğin haberler açılır, akış son hâliyle görünür.
    </div>
  );
}
