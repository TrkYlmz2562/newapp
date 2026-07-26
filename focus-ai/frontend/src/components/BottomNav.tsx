'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';

/** PRD section 7: Home · Explore · Bookmarks · Learning · Profile. */
const ITEMS = [
  { href: '/', label: 'Ana Sayfa', icon: HomeIcon },
  { href: '/explore', label: 'Keşfet', icon: CompassIcon },
  { href: '/bookmarks', label: 'Kayıtlar', icon: BookmarkIcon },
  { href: '/learning', label: 'Öğren', icon: SparkIcon },
  { href: '/profile', label: 'Profil', icon: UserIcon },
] as const;

export function BottomNav() {
  const pathname = usePathname();

  return (
    <nav
      aria-label="Ana gezinme"
      className="fixed inset-x-0 bottom-0 z-40 border-t border-ink-200/80 bg-white/95 backdrop-blur
                 dark:border-ink-800 dark:bg-ink-950/95"
      style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}
    >
      <ul className="mx-auto flex w-full max-w-3xl">
        {ITEMS.map(({ href, label, icon: Icon }) => {
          // Only "/" needs an exact match; every other tab owns its subtree.
          const active = href === '/' ? pathname === '/' : pathname.startsWith(href);

          return (
            <li key={href} className="flex-1">
              <Link
                href={href}
                aria-current={active ? 'page' : undefined}
                className={`flex flex-col items-center gap-1 py-2.5 text-[11px] font-medium transition ${
                  active
                    ? 'text-focus-600 dark:text-focus-400'
                    : 'text-ink-500 hover:text-ink-800 dark:text-ink-400 dark:hover:text-ink-200'
                }`}
              >
                <Icon className="h-5 w-5" filled={active} />
                {label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

interface IconProps {
  className?: string;
  filled?: boolean;
}

function HomeIcon({ className, filled }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill={filled ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z" />
    </svg>
  );
}

function CompassIcon({ className, filled }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
      <circle cx="12" cy="12" r="9" fill={filled ? 'currentColor' : 'none'} opacity={filled ? 0.15 : 1} />
      <path strokeLinecap="round" strokeLinejoin="round" d="m15.5 8.5-2 5-5 2 2-5z" />
    </svg>
  );
}

function BookmarkIcon({ className, filled }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill={filled ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
      <path strokeLinecap="round" strokeLinejoin="round" d="M6 4h12v17l-6-4-6 4z" />
    </svg>
  );
}

function SparkIcon({ className, filled }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill={filled ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
      <path strokeLinecap="round" strokeLinejoin="round" d="M12 3l2.1 5.4L19.5 10l-5.4 2.1L12 17.5 9.9 12.1 4.5 10l5.4-1.6z" />
    </svg>
  );
}

function UserIcon({ className, filled }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
      <circle cx="12" cy="8" r="3.5" fill={filled ? 'currentColor' : 'none'} opacity={filled ? 0.2 : 1} />
      <path strokeLinecap="round" d="M4.5 20a7.5 7.5 0 0 1 15 0" />
    </svg>
  );
}
