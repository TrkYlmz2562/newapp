'use client';

import Link from 'next/link';
import { useState } from 'react';
import { api } from '@/lib/api';
import {
  CATEGORY_ART,
  CATEGORY_LABELS,
  CATEGORY_SLUG,
  meaningful,
  pluralizeSources,
  timeAgo,
  trustBand,
} from '@/lib/format';
import type { StoryCard as Story } from '@/lib/types';
import { StoryVisual } from './StoryVisual';
import { useAuth } from './AuthProvider';

interface Props {
  story: Story;
  rank?: number;
  variant?: 'default' | 'hero' | 'compact';
}

const METER_TONE: Record<'good' | 'ok' | 'warn', string> = {
  good: 'text-signal-trust',
  ok: 'text-signal-caution',
  warn: 'text-signal-hype',
};

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

  const visualHeight = isHero ? 'h-52 sm:h-56' : isCompact ? 'h-24' : 'h-40 sm:h-44';
  const headlineSize = isHero ? 'text-2xl sm:text-[26px]' : isCompact ? 'text-base' : 'text-lg sm:text-xl';

  // Suppressed when it merely repeats the headline — see meaningful().
  const blurb = meaningful(story.summary, story.title) ?? meaningful(story.dek, story.title);

  const [ledColor] = CATEGORY_ART[story.category] ?? CATEGORY_ART.Unknown;

  // Trust as a monospace block meter: 0–100 → ten blocks, coloured by band.
  const filled = Math.max(0, Math.min(10, Math.round(story.trustScore / 10)));
  const tone = METER_TONE[trustBand(story.trustScore).tone];

  return (
    <article className="card animate-fade-up overflow-hidden">
      <Link href={`/story/${story.slug}`} className="block">
        {/* Terminal title bar */}
        <div className="flex items-center gap-2 border-b border-[#1b2534] bg-[#0c1017] px-3 py-2 font-mono text-[11px] text-[#8aa0bd]">
          <span
            className="h-2 w-2 flex-none rounded-full"
            style={{ background: ledColor, boxShadow: `0 0 7px ${ledColor}`, filter: 'saturate(1.4) brightness(1.4)' }}
            aria-hidden="true"
          />
          {rank !== undefined && (
            <span className="font-semibold text-[#cdd8e8]">{String(rank).padStart(2, '0')}</span>
          )}
          <span>
            focus:~/<span className="font-semibold text-[#cdd8e8]">{CATEGORY_SLUG[story.category]}</span>
          </span>
          <span className="ml-auto text-[#5f7089]">{pluralizeSources(story.sourceCount)}</span>
        </div>

        <StoryVisual story={story} className={`w-full ${visualHeight}`} />

        <div className="p-4 sm:p-5">
          {/* Command-line kicker */}
          <div className="flex items-center font-mono text-[11px] uppercase tracking-[0.13em] text-ink-500 dark:text-ink-400">
            <span className="mr-[7px] font-bold text-focus-600 dark:text-focus-400">›</span>
            {CATEGORY_LABELS[story.category]} · {timeAgo(story.publishedAt)}
            {isHero && (
              <span
                className="ml-1.5 inline-block h-[13px] w-[7px] translate-y-[2px] animate-blink bg-focus-600 motion-reduce:hidden dark:bg-focus-400"
                aria-hidden="true"
              />
            )}
          </div>

          <h3
            className={`mt-2.5 font-serif font-semibold leading-tight tracking-tight text-ink-900 dark:text-ink-50 ${headlineSize}`}
          >
            {story.title}
          </h3>

          {!isCompact && blurb && (
            <p className="mt-2 line-clamp-2 font-serif text-[14.5px] leading-relaxed text-ink-600 dark:text-ink-300">
              {blurb}
            </p>
          )}

          {isHero && story.whyItMatters && (
            <p className="mt-3 rounded-lg border-l-2 border-focus-500 bg-focus-50 p-3 font-serif text-sm text-focus-900 dark:bg-focus-900/30 dark:text-focus-100">
              <span className="font-sans text-xs font-semibold uppercase tracking-wide">Neden önemli — </span>
              {story.whyItMatters}
            </p>
          )}

          <div className="my-3 h-px bg-ink-900/10 dark:bg-white/10" />

          {/* Monospace trust meter */}
          <div className="flex items-center gap-2 font-mono text-xs text-ink-500 dark:text-ink-400">
            <span>güven</span>
            <span className="tracking-[1px]" aria-label={`Güven skoru ${story.trustScore}`}>
              <span className={tone}>{'▓'.repeat(filled)}</span>
              <span className="text-ink-300 dark:text-ink-700">{'░'.repeat(10 - filled)}</span>
            </span>
            <span className="font-bold text-ink-900 dark:text-ink-50">{story.trustScore}</span>
            <span className="ml-auto">{story.readingMinutes} dk</span>
            {user && (
              <button
                type="button"
                onClick={toggleSave}
                disabled={busy}
                aria-pressed={saved}
                aria-label={saved ? 'Kayıtlardan çıkar' : 'Kaydet'}
                className={`-my-1 rounded-lg p-1.5 transition hover:bg-ink-100 dark:hover:bg-ink-800 ${
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
          </div>

          {!isCompact && story.topics.length > 0 && (
            <div className="mt-2 flex flex-wrap items-center gap-x-2.5 gap-y-1 font-mono text-[11.5px] text-ink-500 dark:text-ink-400">
              <span className="text-focus-600 dark:text-focus-400">›</span>
              {story.topics.slice(0, 3).map((topic) => (
                <span key={topic.id} className="text-ink-600 dark:text-ink-300">
                  {topic.name}
                </span>
              ))}
            </div>
          )}

          {story.reason && (
            <p className="mt-2 font-mono text-[11px] italic text-ink-400 dark:text-ink-500">{story.reason}</p>
          )}
        </div>
      </Link>
    </article>
  );
}

export function StoryCardSkeleton() {
  return (
    <div className="card overflow-hidden">
      <div className="h-8 w-full bg-[#0c1017]" />
      <div className="skeleton h-40 w-full rounded-none" />
      <div className="space-y-3 p-5">
        <div className="skeleton h-3 w-40" />
        <div className="skeleton h-5 w-full" />
        <div className="skeleton h-5 w-4/5" />
        <div className="skeleton h-3 w-full" />
      </div>
    </div>
  );
}
