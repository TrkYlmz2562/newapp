'use client';

import { useState } from 'react';
import { CATEGORY_ART, CATEGORY_EMOJI, CATEGORY_LABELS } from '@/lib/format';
import type { ContentCategory } from '@/lib/types';

interface VisualStory {
  heroImageUrl?: string | null;
  category: ContentCategory;
  slug: string;
}

interface Props {
  story: VisualStory;
  className?: string;
  emojiClassName?: string;
}

/**
 * Deterministic 0..mod from a string, so a given story always draws the same tile
 * (a random seed would reshuffle the gradient on every render).
 */
function seededInt(seed: string, mod: number): number {
  let hash = 0;
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
  }
  return hash % mod;
}

/**
 * The story's visual: the real publisher/OG image when we have one, otherwise a
 * generated category illustration (gradient + emoji + label). A story without an
 * image is never hidden or penalised — it simply shows the illustration. If a
 * remote image 404s or is blocked, onError falls back to the same illustration
 * instead of leaving a broken-image icon.
 */
export function StoryVisual({ story, className = '', emojiClassName = 'text-5xl' }: Props) {
  const [broken, setBroken] = useState(false);
  const showImage = Boolean(story.heroImageUrl) && !broken;

  if (showImage) {
    return (
      <div className={`relative overflow-hidden bg-ink-100 dark:bg-ink-800 ${className}`}>
        {/* eslint-disable-next-line @next/next/no-img-element -- publisher images come
            from arbitrary domains; see next.config.mjs. */}
        <img
          src={story.heroImageUrl as string}
          alt=""
          loading="lazy"
          onError={() => setBroken(true)}
          className="absolute inset-0 h-full w-full object-cover"
        />
      </div>
    );
  }

  const [from, to] = CATEGORY_ART[story.category] ?? CATEGORY_ART.Unknown;
  const angle = 115 + seededInt(story.slug, 90); // 115°..204°, varied per story

  return (
    <div
      aria-hidden
      className={`relative overflow-hidden ${className}`}
      style={{
        backgroundImage: `radial-gradient(circle at 1px 1px, rgba(255,255,255,0.16) 1px, transparent 0), linear-gradient(${angle}deg, ${from}, ${to})`,
        backgroundSize: '14px 14px, cover',
      }}
    >
      <div
        className="absolute inset-0 flex flex-col items-center justify-center gap-1.5 text-white"
        // A soft text shadow keeps the emoji and label legible on the lighter
        // category gradients (e.g. Startup/Tools), where white alone dips under
        // the WCAG AA contrast floor.
        style={{ textShadow: '0 1px 3px rgba(0,0,0,0.45)' }}
      >
        <span className={emojiClassName}>{CATEGORY_EMOJI[story.category]}</span>
        <span className="text-[11px] font-semibold uppercase tracking-[0.18em]">
          {CATEGORY_LABELS[story.category]}
        </span>
      </div>
    </div>
  );
}
