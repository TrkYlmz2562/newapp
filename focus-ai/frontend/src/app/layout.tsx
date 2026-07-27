import type { Metadata, Viewport } from 'next';
import { Literata } from 'next/font/google';
import './globals.css';
import { BottomNav } from '@/components/BottomNav';
import { AuthProvider } from '@/components/AuthProvider';
import { OfflineBanner } from '@/components/OfflineBanner';
import { SpeechDock } from '@/components/SpeechDock';
import { ServiceWorkerRegistrar } from '@/components/ServiceWorkerRegistrar';
import { themeBootstrapScript } from '@/components/ThemeToggle';

/*
 * The headline face, pinned.
 *
 * Nothing was loading one before: tailwind asked for `serif`, and Tailwind's
 * default stack leads with `ui-serif`, so the app was set in whatever the device
 * happened to call a serif — New York on modern iOS, Georgia on Android and
 * desktop, Times on older iOS. Three different faces with three different sets
 * of metrics, which means the same story is a different number of lines and the
 * same card is a different height depending on who opens it.
 *
 * The other half of the defect: `font-semibold` asks for weight 600, and Georgia
 * and Times only have 400 and 700. The browser was either synthesising a
 * smeared faux-bold or snapping to 700. A variable face has the real 600.
 *
 * Literata is chosen to change as little as possible. It is a screen-reading
 * serif with a generous x-height, so it sits between the New York the reader
 * sees on the phone and the Georgia everything else falls back to — the identity
 * stays exactly where it was, it just stops moving.
 *
 * Only `serif` is touched. `sans` keeps the system UI stack and `mono` keeps the
 * system monospace, both of which resolve well and are part of the look.
 */
const literata = Literata({
  subsets: ['latin', 'latin-ext'],
  style: ['normal', 'italic'],
  variable: '--font-serif',
  display: 'swap',
});

export const metadata: Metadata = {
  title: {
    default: 'Focus AI — Günün bilmen gereken teknoloji gelişmeleri',
    template: '%s · Focus AI',
  },
  description:
    'Onlarca kaynaktan toplanan teknoloji ve yapay zekâ gelişmelerini, yapay zekâ ile birleştirilmiş ' +
    've özetlenmiş biçimde günde 5 dakikada oku.',
  manifest: '/manifest.webmanifest',
  icons: {
    icon: [{ url: '/favicon.svg', type: 'image/svg+xml' }],
    apple: [{ url: '/icons/icon-192.svg' }],
  },
  applicationName: 'Focus AI',
  appleWebApp: {
    capable: true,
    title: 'Focus AI',
    statusBarStyle: 'default',
  },
  formatDetection: { telephone: false },
};

export const viewport: Viewport = {
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: '#f6f7f9' },
    { media: '(prefers-color-scheme: dark)', color: '#080b12' },
  ],
  width: 'device-width',
  initialScale: 1,
  // Zoom stays enabled: disabling it is an accessibility failure, and the layout
  // is designed to tolerate it.
  maximumScale: 5,
  viewportFit: 'cover',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="tr" className={literata.variable} suppressHydrationWarning>
      <head>
        {/*
          Blocking on purpose: the theme class must be on <html> before the first
          paint, or every load flashes light before React catches up.
        */}
        <script dangerouslySetInnerHTML={{ __html: themeBootstrapScript }} />
      </head>
      <body className="min-h-dvh">
        <AuthProvider>
          <div className="mx-auto flex min-h-dvh w-full max-w-3xl flex-col">
            <OfflineBanner />
            {/* The dock is fixed and cannot push anything, so the page reserves
                its measured height on top of the tab bar's. --dock-h is set by
                the dock itself and absent whenever nothing is playing. */}
            <main className="flex-1 pb-[calc(6rem+var(--dock-h,0px))]">{children}</main>
            <BottomNav />
            <SpeechDock />
          </div>
        </AuthProvider>
        <ServiceWorkerRegistrar />
      </body>
    </html>
  );
}
