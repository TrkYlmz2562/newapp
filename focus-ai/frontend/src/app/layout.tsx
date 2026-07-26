import type { Metadata, Viewport } from 'next';
import './globals.css';
import { BottomNav } from '@/components/BottomNav';
import { AuthProvider } from '@/components/AuthProvider';
import { OfflineBanner } from '@/components/OfflineBanner';
import { ServiceWorkerRegistrar } from '@/components/ServiceWorkerRegistrar';
import { themeBootstrapScript } from '@/components/ThemeToggle';

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
    <html lang="tr" suppressHydrationWarning>
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
            <main className="flex-1 pb-24">{children}</main>
            <BottomNav />
          </div>
        </AuthProvider>
        <ServiceWorkerRegistrar />
      </body>
    </html>
  );
}
