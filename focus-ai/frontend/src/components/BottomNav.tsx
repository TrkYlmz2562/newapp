'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { trUpper } from '@/lib/format';

/**
 * PRD section 7, set as a section strip rather than an icon bar.
 *
 * Five hand-drawn glyphs were carrying no information the label underneath did
 * not already carry — a compass does not mean "Keşfet" to anyone who has not
 * already read the word. Dropping them takes the strip from 57px to a typographic
 * rule and removes 40 lines of SVG, while the tap target stays above 44px because
 * the padding does the work the icon used to.
 *
 * Finance is deliberately absent: a finance development is a story like any other
 * and belongs in the feed, competing on importance. It reaches the reader as a
 * section in the front-page index instead.
 */
const ITEMS = [
  { href: '/', label: 'Bugün' },
  { href: '/explore', label: 'Keşfet' },
  { href: '/bookmarks', label: 'Kayıtlar' },
  { href: '/learning', label: 'Öğren' },
  { href: '/profile', label: 'Profil' },
] as const;

export function BottomNav() {
  const pathname = usePathname();

  return (
    <nav
      aria-label="Ana gezinme"
      className="fixed inset-x-0 bottom-0 z-40 border-t-2 border-ink-900 bg-ink-50/95 backdrop-blur
                 dark:border-ink-100 dark:bg-ink-950/95"
      style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}
    >
      <ul className="mx-auto flex w-full max-w-3xl">
        {ITEMS.map(({ href, label }) => {
          // Only "/" needs an exact match; every other tab owns its subtree.
          const active = href === '/' ? pathname === '/' : pathname.startsWith(href);

          return (
            <li key={href} className="flex-1">
              <Link
                href={href}
                aria-current={active ? 'page' : undefined}
                className={`block py-3.5 text-center font-sans text-[10.5px] font-bold tracking-[0.1em] transition ${
                  active
                    ? 'text-focus-600 dark:text-focus-300'
                    : 'text-ink-500 hover:text-ink-900 dark:text-ink-400 dark:hover:text-ink-100'
                }`}
                style={{ fontVariationSettings: "'wdth' 78" }}
              >
                {trUpper(label)}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
