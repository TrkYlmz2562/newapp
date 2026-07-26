'use client';

import Link from 'next/link';
import { useState } from 'react';
import { api } from '@/lib/api';
import {
  CATEGORY_EMOJI,
  CATEGORY_LABELS,
  meaningful,
  pluralizeSources,
  readingTime,
  timeAgo,
} from '@/lib/format';
import type { StoryCard as Story } from '@/lib/types';
import { StoryVisual } from './StoryVisual';
import { TrustChip } from './TrustBadge';
import { useAuth } from './AuthProvider';

interface Props {
  story: Story;
  rank?: number;
  variant?: 'default' | 'hero' | 'compact';
}

export function StoryCard({ story, rank, variant = 'default' }: Props) {
  const { user } = useAuth();
  const [saved, setSaved] = useState(story.isBookmarked);
  const [busy, setBusy] = useState(false);

  const toggleSave = async (event: React.MouseEvent) => {
    // The card is a link; saving must not navigate.
    event.preventDefault();
    event.stopPropagation();

    if (!user || busy) return;

    // Optimistic: the toggle is cheap to reverse and the latency is visible.
    const next = !saved;
    setSaved(next);
    setBusy(true);

    try {
      const result = await api.bookmarks.toggle(story.id);
      setSaved(result.saved);
    } catch {
      setSaved(!next);
    } finally {
      setBusy(false);
    }
  };

  const isHero = variant === 'hero';
  const isCompact = variant === 'compact';

  // A full-bleed preview banner at the top of the card, Twitter-style: the real
  // image when we have one, a category illustration otherwise.
  const bannerHeight = isHero ? 'h-48 sm:h-56' : isCompact ? 'h-28' : 'h-40 sm:h-44';
  const emojiSize = isHero ? 'text-6xl' : isCompact ? 'text-4xl' : 'text-5xl';

  // Suppressed when it merely repeats the headline — see meaningful().
  const blurb = meaningful(story.summary, story.title) ?? meaningful(story.dek, story.title);

  return (
    <article className="card animate-fade-up overflow-hidden">
      <Link href={`/story/${story.slug}`} className="block">
        <StoryVisual
          story={story}
          className={`w-full ${bannerHeight}`}
          emojiClassName={emojiSize}
        />

        <div className="p-4 sm:p-5">
        <header className="mb-2 flex flex-wrap items-center gap-2 text-xs text-ink-500 dark:text-ink-400">
          {rank !== undefined && (
            <span className="grid h-5 w-5 place-items-center rounded-md bg-ink-900 text-[11px] font-bold text-white dark:bg-ink-100 dark:text-ink-900">
              {rank}
            </span>
          )}
          <span className="chip bg-ink-100 text-ink-600 dark:bg-ink-800 dark:text-ink-300">
            {CATEGORY_EMOJI[story.category]} {CATEGORY_LABELS[story.category]}
          </span>
          <TrustChip score={story.trustScore} />
          <span>·</span>
          <time dateTime={story.publishedAt}>{timeAgo(story.publishedAt)}</time>
          <span>·</span>
          <span>{pluralizeSources(story.sourceCount)}</span>
        </header>

        <h3
          className={`font-semibold leading-snug text-ink-900 dark:text-ink-50 ${
            isHero ? 'text-xl sm:text-2xl' : 'text-base sm:text-lg'
          }`}
        >
          {story.title}
        </h3>

        {variant !== 'compact' && blurb && (
          <p className="mt-2 line-clamp-3 text-sm leading-relaxed text-ink-600 dark:text-ink-300">
            {blurb}
          </p>
        )}

        {isHero && story.whyItMatters && (
          <p className="mt-3 rounded-xl bg-focus-50 p-3 text-sm text-focus-900 dark:bg-focus-900/30 dark:text-focus-100">
            <span className="font-semibold">Neden önemli? </span>
            {story.whyItMatters}
          </p>
        )}

        <footer className="mt-3 flex flex-wrap items-center gap-2">
          {story.topics.slice(0, 3).map((topic) => (
            <span
              key={topic.id}
              className="chip bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200"
            >
              {topic.name}
            </span>
          ))}

          <span className="ml-auto flex items-center gap-3 text-xs text-ink-500 dark:text-ink-400">
            <span>{readingTime(story.readingMinutes)}</span>

            {user && (
              <button
                type="button"
                onClick={toggleSave}
                disabled={busy}
                aria-pressed={saved}
                aria-label={saved ? 'Kayıtlardan çıkar' : 'Kaydet'}
                className={`rounded-lg p-1.5 transition hover:bg-ink-100 dark:hover:bg-ink-800 ${
                  saved ? 'text-focus-600 dark:text-focus-400' : ''
                }`}
              >
                <svg
                  className="h-4 w-4"
                  viewBox="0 0 24 24"
                  fill={saved ? 'currentColor' : 'none'}
                  stroke="currentColor"
                  strokeWidth={1.8}
                  aria-hidden="true"
                >
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 4h12v17l-6-4-6 4z" />
                </svg>
              </button>
            )}
          </span>
        </footer>

        {story.reason && (
          <p className="mt-2 text-xs italic text-ink-400 dark:text-ink-500">{story.reason}</p>
        )}
        </div>
      </Link>
    </article>
  );
}

export function StoryCardSkeleton() {
  return (
    <div className="card overflow-hidden">
      <div className="skeleton h-40 w-full rounded-none" />
      <div className="space-y-3 p-5">
        <div className="skeleton h-4 w-32" />
        <div className="skeleton h-5 w-full" />
        <div className="skeleton h-5 w-4/5" />
        <div className="skeleton h-3 w-full" />
        <div className="skeleton h-3 w-3/4" />
      </div>
    </div>
  );
}
