'use client';

import { Suspense, useCallback, useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { PageHeader, EmptyState, ErrorState } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { api } from '@/lib/api';
import { CATEGORY_EMOJI, CATEGORY_LABELS } from '@/lib/format';
import type { ContentCategory, StoryCard as Story } from '@/lib/types';

const CATEGORIES: ContentCategory[] = [
  'Ai',
  'Software',
  'OpenSource',
  'Tools',
  'Security',
  'Startup',
  'Science',
  'Hardware',
  'Career',
];

function ExploreContent() {
  const router = useRouter();
  const params = useSearchParams();
  const active = (params.get('category') as ContentCategory | null) ?? null;

  const [stories, setStories] = useState<Story[]>([]);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(
    async (targetPage: number, replace: boolean) => {
      setLoading(true);
      setError(null);

      try {
        const result = await api.stories.feed({
          category: active ?? undefined,
          page: targetPage,
          pageSize: 20,
        });

        setStories((current) => (replace ? result.items : [...current, ...result.items]));
        setHasMore(result.hasNextPage);
        setPage(result.page);
      } catch {
        setError('Haberler yüklenemedi.');
      } finally {
        setLoading(false);
      }
    },
    [active],
  );

  useEffect(() => {
    void load(1, true);
  }, [load]);

  const selectCategory = (category: ContentCategory | null) => {
    router.push(category ? `/explore?category=${category}` : '/explore');
  };

  return (
    <div className="space-y-4">
      <PageHeader title="Keşfet" subtitle="Kategoriye göre tüm gelişmeler" />

      <div role="tablist" aria-label="Kategori filtresi" className="flex gap-2 overflow-x-auto px-4 pb-1 sm:px-5">
        <button
          type="button"
          role="tab"
          aria-selected={active === null}
          onClick={() => selectCategory(null)}
          className={`chip shrink-0 border px-3 py-1.5 text-sm ${
            active === null
              ? 'border-focus-500 bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200'
              : 'border-ink-200 bg-white text-ink-700 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200'
          }`}
        >
          Tümü
        </button>

        {CATEGORIES.map((category) => (
          <button
            key={category}
            type="button"
            role="tab"
            aria-selected={active === category}
            onClick={() => selectCategory(category)}
            className={`chip shrink-0 border px-3 py-1.5 text-sm ${
              active === category
                ? 'border-focus-500 bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200'
                : 'border-ink-200 bg-white text-ink-700 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200'
            }`}
          >
            {CATEGORY_EMOJI[category]} {CATEGORY_LABELS[category]}
          </button>
        ))}
      </div>

      {error && <ErrorState message={error} onRetry={() => load(1, true)} />}

      <div className="space-y-3 px-4 sm:px-5">
        {stories.map((story) => (
          <StoryCard key={story.id} story={story} />
        ))}

        {loading && (
          <>
            <StoryCardSkeleton />
            <StoryCardSkeleton />
          </>
        )}

        {!loading && stories.length === 0 && !error && (
          <EmptyState
            title="Bu kategoride henüz haber yok"
            description="Kaynaklar tarandıkça burası dolacak. Başka bir kategoriye göz atabilirsin."
          />
        )}

        {hasMore && !loading && (
          <button type="button" onClick={() => load(page + 1, false)} className="btn-ghost w-full">
            Daha fazla göster
          </button>
        )}
      </div>
    </div>
  );
}

export default function ExplorePage() {
  // useSearchParams needs a Suspense boundary for static prerendering.
  return (
    <Suspense fallback={<div className="space-y-3 px-4 pt-20 sm:px-5"><StoryCardSkeleton /></div>}>
      <ExploreContent />
    </Suspense>
  );
}
