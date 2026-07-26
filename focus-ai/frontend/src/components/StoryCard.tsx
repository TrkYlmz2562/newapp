'use client';

import Link from 'next/link';
import { useState } from 'react';
import { api } from '@/lib/api';
import {
  CATEGORY_LABELS,
  meaningful,
  pluralizeSources,
  timeAgo,
  trUpper,
  trustBand,
} from '@/lib/format';
import { forgetOffline, saveForOffline } from '@/lib/offline';
import type { StoryCard as Story } from '@/lib/types';
import { CommitmentBadge } from './CommitmentBadge';
import { FeedbackButtons } from './FeedbackButtons';
import { useAuth } from './AuthProvider';

interface Props {
  story: Story;
  rank?: number;
  variant?: 'default' | 'hero' | 'compact';
}

/**
 * A story as a ruled item on a printed page.
 *
 * The card this replaces spent 41% of its height before the headline started: a
 * terminal title bar, then a full-width image plate whose only content — on the
 * majority of stories, which have no photo — was the top topic name, reprinted a
 * paragraph lower in the topics row. The category was stated four times over.
 * Measured result: 1.4 stories per phone screen, on a product whose pitch is
 * "fewer stories, each one justified".
 *
 * What replaces it: a folio rail carrying the rank, the headline at the top where
 * the eye lands, and one byline. A photo, when there is one, is a 72px cut on the
 * right rather than a band across the page — so it competes with nothing, and its
 * absence costs nothing.
 */
export function StoryCard({ story, rank, variant = 'default' }: Props) {
  const { user } = useAuth();
  const [saved, setSaved] = useState(story.isBookmarked);
  const [busy, setBusy] = useState(false);

  const toggleSave = async (event: React.MouseEvent) => {
    // The link is a sibling overlay, not an ancestor, so nothing would bubble to
    // it. Kept as a guard in case this button is ever nested in one again.
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

      // Saved means "I want this later", and later is exactly when there is no
      // signal. Best-effort and silent — see lib/offline.
      void (result.saved ? saveForOffline(story.slug) : forgetOffline(story.slug));
    } catch {
      setSaved(!next);
    } finally {
      setBusy(false);
    }
  };

  const isHero = variant === 'hero';
  const isCompact = variant === 'compact';

  // Suppressed when it merely repeats the headline — see meaningful().
  const blurb = meaningful(story.summary, story.title) ?? meaningful(story.dek, story.title);
  const band = trustBand(story.trustScore);

  const headline = isHero
    ? 'text-[clamp(30px,8.4vw,42px)] leading-[1.0] tracking-[-0.015em]'
    : isCompact
      ? 'text-base leading-[1.2]'
      : 'text-[clamp(19px,5.2vw,23px)] leading-[1.14] tracking-[-0.01em]';

  const bandTone =
    band.tone === 'good'
      ? 'text-signal-trust'
      : band.tone === 'ok'
        ? 'text-signal-caution'
        : 'text-signal-hype';

  return (
    <article className="relative">
      {/*
        The whole item navigates, but the link is an overlay rather than a
        wrapper: wrapping put <button> elements inside an <a>, which is invalid
        HTML and made iOS raise its "Open Link" sheet on a long press. Content
        above ignores pointer events so taps fall through; controls opt back in.
      */}
      <Link href={`/story/${story.slug}`} className="absolute inset-0 z-0" aria-label={story.title} />

      <div className="pointer-events-none relative z-10 flex gap-3 py-5">
        {/*
          The folio rail. A newspaper numbers its items and rules the column;
          together they say "this is a list on a page" without a box per entry.
        */}
        {rank !== undefined && (
          <div className="flex-none border-r border-ink-200 pr-3 dark:border-ink-800">
            <span className="figure block w-6 text-[22px] leading-none text-focus-600 dark:text-focus-300">
              {String(rank).padStart(2, '0')}
            </span>
          </div>
        )}

        <div className="min-w-0 flex-1">
          {/* Section flag. Condensed caps, because Turkish labels run long. */}
          <div
            className="flex flex-wrap items-baseline gap-x-2 font-sans text-[11px] font-semibold tracking-[0.1em] text-focus-600 dark:text-focus-300"
            style={{ fontVariationSettings: "'wdth' 82" }}
          >
            <span>{trUpper(CATEGORY_LABELS[story.category])}</span>
            <span className="text-ink-400 dark:text-ink-500">{trUpper(timeAgo(story.publishedAt))}</span>
            {story.isRead && (
              // The ranker quarters the score of a story you have opened. Saying
              // so is what turns "why is this so far down" into an answer.
              <span
                className="text-ink-400 dark:text-ink-500"
                title="Bu haberi okudun; akışta aşağı alınır"
              >
                {trUpper('okundu')}
              </span>
            )}
          </div>

          <div className="mt-1.5 flex gap-3">
            <div className="min-w-0 flex-1">
              <h3
                className={`text-balance font-serif font-semibold ${headline} ${
                  story.isRead ? 'text-ink-500 dark:text-ink-400' : 'text-ink-900 dark:text-ink-50'
                }`}
              >
                {story.title}
              </h3>

              {!isCompact && blurb && (
                <p className="mt-2 line-clamp-2 font-serif text-[17px] italic leading-[1.45] text-ink-600 dark:text-ink-300">
                  {blurb}
                </p>
              )}
            </div>

            {/*
              The picture is a cut, not a band. It used to be a full-width 160px
              plate that most stories had to fake with a topic name; at 72px it
              earns its place when a real photo exists and is simply absent when
              one does not.
            */}
            {story.heroImageUrl && !isHero && (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={story.heroImageUrl}
                alt=""
                loading="lazy"
                className="h-[72px] w-[72px] flex-none object-cover"
              />
            )}
          </div>

          {isHero && story.heroImageUrl && (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={story.heroImageUrl} alt="" className="mt-3 h-44 w-full object-cover" />
          )}

          {isHero && story.whyItMatters && (
            <p className="mt-3 border-l-2 border-focus-600 pl-3 font-serif text-[16px] leading-[1.5] text-ink-700 dark:text-ink-200">
              <span className="font-sans text-[11px] font-bold tracking-[0.1em] text-focus-600 dark:text-focus-300">
                {trUpper('Neden önemli')}{' '}
              </span>
              {story.whyItMatters}
            </p>
          )}

          {/*
            The byline: what used to be four stacked rows — trust meter, reading
            time, commitment chip, topics — on one line, in the order a reader
            asks for it.

            The block meter went with the terminal. A figure plus the band's own
            word says the same thing in less space, and survives a font
            substitution — which ▓░ does not, since a face without Block Elements
            makes iOS borrow them at a different advance width and the ten cells
            stop lining up between cards.
          */}
          <div
            className="mt-2.5 flex flex-wrap items-center gap-x-2.5 gap-y-1 border-t border-ink-200 pt-2 font-sans text-[12px] text-ink-500 dark:border-ink-800 dark:text-ink-400"
            style={{ fontVariationSettings: "'wdth' 88" }}
          >
            <span className="figure text-ink-900 dark:text-ink-100">{story.trustScore}</span>
            <span className={bandTone}>{band.label}</span>
            <span aria-hidden="true">·</span>
            <span>{pluralizeSources(story.sourceCount)}</span>
            <span aria-hidden="true">·</span>
            <span>{story.readingMinutes} dk</span>

            {story.commitment && <CommitmentBadge commitment={story.commitment} />}

            <span className="ml-auto flex items-center gap-1">
              {!isCompact && (
                <FeedbackButtons
                  storyId={story.id}
                  initial={story.feedback}
                  surface="card"
                  className="pointer-events-auto -my-1"
                />
              )}
              {user && (
                <button
                  type="button"
                  onClick={toggleSave}
                  disabled={busy}
                  aria-pressed={saved}
                  aria-label={saved ? 'Kayıtlardan çıkar' : 'Kaydet'}
                  className={`tap-row pointer-events-auto -my-1 p-1.5 transition ${
                    saved ? 'text-focus-600 dark:text-focus-300' : 'text-ink-400 dark:text-ink-500'
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
          </div>

          {story.reason && (
            <p className="mt-1.5 font-serif text-[13px] italic text-ink-400 dark:text-ink-500">
              {story.reason}
            </p>
          )}
        </div>
      </div>
    </article>
  );
}

export function StoryCardSkeleton() {
  return (
    <div className="flex gap-3 border-t border-ink-200 py-5 dark:border-ink-800">
      <div className="skeleton h-6 w-6 flex-none" />
      <div className="min-w-0 flex-1 space-y-2">
        <div className="skeleton h-3 w-32" />
        <div className="skeleton h-5 w-full" />
        <div className="skeleton h-5 w-4/5" />
        <div className="skeleton h-3 w-40" />
      </div>
    </div>
  );
}
