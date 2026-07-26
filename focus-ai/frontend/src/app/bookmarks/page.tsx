'use client';

import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { EmptyState, ErrorState, PageHeader, SignInPrompt } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { api } from '@/lib/api';
import type { StoryCard as Story } from '@/lib/types';

interface Bookmark {
  id: string;
  note?: string | null;
  tags: string[];
  createdAt: string;
  story: Story;
}

export default function BookmarksPage() {
  const { user, loading: authLoading } = useAuth();
  const [bookmarks, setBookmarks] = useState<Bookmark[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!user) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const result = await api.bookmarks.list(1, 50);
      setBookmarks(result.items);
    } catch {
      setError('Kayıtlar yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }, [user]);

  useEffect(() => {
    if (!authLoading) void load();
  }, [authLoading, load]);

  if (!authLoading && !user) {
    return (
      <div className="space-y-4">
        <PageHeader title="Kayıtlar" />
        <SignInPrompt message="Kaydettiğin haberleri görmek için giriş yapmalısın." />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Kayıtlar"
        subtitle={bookmarks.length > 0 ? `${bookmarks.length} kayıtlı haber` : undefined}
      />

      {error && <ErrorState message={error} onRetry={load} />}

      <div className="space-y-3 px-4 sm:px-5">
        {loading || authLoading ? (
          <>
            <StoryCardSkeleton />
            <StoryCardSkeleton />
          </>
        ) : bookmarks.length === 0 ? (
          <EmptyState
            title="Henüz kayıt yok"
            description="Bir haberi sonra okumak için kartındaki yer imi simgesine dokun."
          />
        ) : (
          bookmarks.map((bookmark) => (
            <div key={bookmark.id} className="space-y-1">
              <StoryCard story={{ ...bookmark.story, isBookmarked: true }} />
              {bookmark.note && (
                <p className="px-2 text-xs italic text-ink-500 dark:text-ink-400">
                  Notun: {bookmark.note}
                </p>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  );
}
