'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { Masthead, EmptyState, ErrorState } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { api } from '@/lib/api';
import { CATEGORY_LABELS, formatDayHeading, readingTime, trUpper } from '@/lib/format';
import type { ContentCategory, Digest, StoryCard as Story } from '@/lib/types';

/** The category shortcuts from PRD section 7. */
const SHORTCUTS: { category: ContentCategory; href: string }[] = [
  { category: 'Ai', href: '/explore?category=Ai' },
  { category: 'Software', href: '/explore?category=Software' },
  { category: 'Startup', href: '/explore?category=Startup' },
  // Finance is a chip like the rest, not a tab. Only grounded finance stories
  // reach the feed at all — see StoryFilters on the server — so this shows what
  // was actually decided rather than everything with a money word in it.
  { category: 'Finance', href: '/explore?category=Finance' },
  { category: 'OpenSource', href: '/explore?category=OpenSource' },
];

export default function HomePage() {
  const { user, loading: authLoading } = useAuth();
  const [digest, setDigest] = useState<Digest | null>(null);
  const [top, setTop] = useState<Story | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);

    try {
      // Both are independent; a slow digest should not delay the hero.
      const [digestResult, topResult] = await Promise.all([
        api.digest.get('Daily').catch(() => undefined),
        api.stories.top(36).catch(() => undefined),
      ]);

      setDigest(digestResult ?? null);
      setTop(topResult ?? null);
    } catch {
      setError('İçerikler yüklenemedi. Bağlantını kontrol edip tekrar dene.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    // Re-run once auth resolves so a signed-in reader gets the personalised
    // edition rather than the shared one.
    if (!authLoading) void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authLoading, user?.id]);

  const heroStory = digest?.items[0]?.story ?? top;
  const rest = digest?.items.slice(1) ?? [];

  return (
    <div>
      <Masthead
        dateLine={digest ? formatDayHeading(digest.date) : 'Bugün'}
        note={
          digest
            ? `${digest.items.length} haber · ${readingTime(digest.readingMinutes)}`
            : undefined
        }
      />

      {/*
        The section index, straight off a front page: a single ruled strip of
        section names. It replaces a row of bordered pill chips that each carried
        an emoji and 12px of padding — 36px of height for a navigation the reader
        uses once a session, on the screen the audit found had no room for a
        story. Rules cost 1px.
      */}
      <nav
        aria-label="Bölümler"
        className="chip-row border-b border-ink-300 px-[18px] pb-2 font-sans text-[10.5px] font-semibold tracking-[0.1em] text-ink-500 dark:border-ink-800 dark:text-ink-400"
        style={{ fontVariationSettings: "'wdth' 78" }}
      >
        {SHORTCUTS.map(({ category, href }) => (
          <Link key={category} href={href} className="shrink-0 py-1 hover:text-focus-600 dark:hover:text-focus-300">
            {trUpper(CATEGORY_LABELS[category])}
          </Link>
        ))}
        <Link href="/trends" className="shrink-0 py-1 hover:text-focus-600 dark:hover:text-focus-300">
          {trUpper('Trendler')}
        </Link>
        <Link href="/search" className="shrink-0 py-1 text-focus-600 dark:text-focus-300">
          {trUpper('Ara')}
        </Link>
      </nav>

      {digest?.intro && (
        <p className="px-[18px] font-serif text-[17px] italic leading-[1.5] text-ink-600 dark:text-ink-300">
          {digest.intro}
        </p>
      )}

      {error && <ErrorState message={error} onRetry={load} />}

      {loading ? (
        <div className="divide-y divide-ink-200 px-[18px] dark:divide-ink-800">
          <StoryCardSkeleton />
          <StoryCardSkeleton />
          <StoryCardSkeleton />
        </div>
      ) : heroStory ? (
        <div className="px-[18px]">
          <section aria-label="Günün en önemli gelişmesi" className="border-b border-ink-300 dark:border-ink-800">
            {/* The lede needs no label — its size is the label. */}
            <StoryCard story={heroStory} variant="hero" />
          </section>

          {rest.length > 0 && (
            <section aria-labelledby="digest-list" className="mt-5">
              <h2
                id="digest-list"
                className="border-t-2 border-ink-900 pt-2 font-sans text-[11px] font-bold tracking-[0.12em] text-ink-900 dark:border-ink-100 dark:text-ink-100"
                style={{ fontVariationSettings: "'wdth' 78" }}
              >
                {trUpper(`Günün ${digest?.items.length ?? 0} konusu`)}
              </h2>
              <div className="divide-y divide-ink-200 dark:divide-ink-800">
                {rest.map((entry) => (
                  <StoryCard key={entry.story.id} story={entry.story} rank={entry.rank} />
                ))}
              </div>
            </section>
          )}

          {!user && (
            <div className="border-t-2 border-ink-900 py-5 text-center dark:border-ink-100">
              <p className="text-sm text-ink-600 dark:text-ink-300">
                İlgi alanlarını seçersen bu liste tamamen sana göre sıralanır.
              </p>
              <Link href="/register" className="btn-primary mt-3">
                Ücretsiz hesap oluştur
              </Link>
            </div>
          )}
        </div>
      ) : (
        !error && (
          <EmptyState
            title="Henüz içerik yok"
            description="Kaynaklar ilk kez taranıyor olabilir. Birkaç dakika içinde tekrar dene."
            action={
              <button type="button" onClick={load} className="btn-ghost">
                Yenile
              </button>
            }
          />
        )
      )}
    </div>
  );
}
