'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { PageHeader, SearchBar, EmptyState, ErrorState } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { api } from '@/lib/api';
import { CATEGORY_EMOJI, CATEGORY_LABELS, formatDayHeading, readingTime } from '@/lib/format';
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
    <div className="space-y-6">
      <PageHeader
        title={user ? `Merhaba, ${user.displayName.split(' ')[0]}` : 'Focus AI'}
        subtitle={
          digest
            ? `${formatDayHeading(digest.date)} · ${readingTime(digest.readingMinutes)}`
            : 'Günün bilmen gereken teknoloji gelişmeleri'
        }
      />

      <SearchBar />

      {digest?.intro && (
        <p className="mx-4 rounded-2xl bg-white p-4 text-sm leading-relaxed text-ink-700 shadow-sm dark:bg-ink-900 dark:text-ink-200 sm:mx-5">
          {digest.intro}
        </p>
      )}

      <nav aria-label="Kategoriler" className="flex gap-2 overflow-x-auto px-4 pb-1 sm:px-5">
        {SHORTCUTS.map(({ category, href }) => (
          <Link
            key={category}
            href={href}
            className="chip shrink-0 border border-ink-200 bg-white px-3 py-1.5 text-sm text-ink-700
                       hover:border-focus-300 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200"
          >
            {CATEGORY_EMOJI[category]} {CATEGORY_LABELS[category]}
          </Link>
        ))}
        <Link
          href="/trends"
          className="chip shrink-0 border border-ink-200 bg-white px-3 py-1.5 text-sm text-ink-700
                     hover:border-focus-300 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200"
        >
          📈 Trendler
        </Link>
      </nav>

      {error && <ErrorState message={error} onRetry={load} />}

      {loading ? (
        <div className="space-y-3 px-4 sm:px-5">
          <StoryCardSkeleton />
          <StoryCardSkeleton />
          <StoryCardSkeleton />
        </div>
      ) : heroStory ? (
        <div className="space-y-4 px-4 sm:px-5">
          <section aria-labelledby="top-story">
            <h2 id="top-story" className="mb-2 text-sm font-semibold text-ink-500 dark:text-ink-400">
              🔥 Günün En Önemlisi
            </h2>
            <StoryCard story={heroStory} variant="hero" />
          </section>

          {rest.length > 0 && (
            <section aria-labelledby="digest-list" className="space-y-3">
              <h2 id="digest-list" className="pt-2 text-sm font-semibold text-ink-500 dark:text-ink-400">
                Günün Bilmen Gereken {digest?.items.length ?? 0} Konusu
              </h2>
              {rest.map((entry) => (
                <StoryCard key={entry.story.id} story={entry.story} rank={entry.rank} />
              ))}
            </section>
          )}

          {!user && (
            <div className="card p-5 text-center">
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
