'use client';

import { useState } from 'react';
import { CATEGORY_ACCENT, CATEGORY_ICON, CATEGORY_LABELS } from '@/lib/format';
import type { ContentCategory, Topic } from '@/lib/types';

interface VisualStory {
  heroImageUrl?: string | null;
  visualEntity?: string | null;
  visualKicker?: string | null;
  category: ContentCategory;
  topics?: Topic[];
}

interface Props {
  story: VisualStory;
  className?: string;
  size?: 'compact' | 'default' | 'hero';
}

/** Turkish uppercase: plain toUpperCase turns "girişim" into "GIRIŞIM". */
const upper = (value: string) => value.toLocaleUpperCase('tr-TR');

/**
 * The subject is the whole point of the tile, so it is set as large as it can be
 * without wrapping badly. Short designations ("M5", "o5") become posters; longer
 * ones step down rather than overflow.
 */
function subjectSize(length: number, size: Props['size']): string {
  const scale = size === 'hero' ? 1.18 : size === 'compact' ? 0.62 : 1;
  const base =
    length <= 3 ? [40, 15, 68] : length <= 6 ? [32, 12, 52] : length <= 11 ? [24, 8.5, 38] : [19, 6.5, 29];

  return `clamp(${(base[0] * scale).toFixed(0)}px, ${(base[1] * scale).toFixed(1)}vw, ${(
    base[2] * scale
  ).toFixed(0)}px)`;
}

/**
 * The story's visual. A real publisher/OG image when we have one; otherwise a
 * "headline plate" — the story's own subject set in display type over a tinted
 * category field.
 *
 * The plate is deliberately made of the story's data rather than generic art: a
 * decorative pattern looks the same on every card and therefore says nothing,
 * which is exactly the problem it replaced. A story with no image is never
 * hidden or down-ranked — it simply shows its subject.
 */
export function StoryVisual({ story, className = '', size = 'default' }: Props) {
  const [broken, setBroken] = useState(false);
  const useImage = Boolean(story.heroImageUrl) && !broken;

  if (useImage) {
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

  const accent = CATEGORY_ACCENT[story.category] ?? CATEGORY_ACCENT.Unknown;
  const label = CATEGORY_LABELS[story.category] ?? CATEGORY_LABELS.Unknown;

  // The backend fills visualEntity for every enriched story — from the model, or
  // extracted from the headline when no model ran. The category label is the last
  // resort so the plate is never empty.
  const subject = story.visualEntity?.trim() || label;
  const usedLabelAsSubject = subject === label;

  const topicLine = (story.topics ?? [])
    .slice(0, 2)
    .map((topic) => topic.name)
    .join(' · ');

  // Never repeat the subject underneath itself.
  const kicker = story.visualKicker?.trim() || topicLine || (usedLabelAsSubject ? '' : label);

  // The accent varies per category, so it cannot be a static Tailwind class; it
  // is handed to CSS as a variable and the .accent-* rules in globals.css pick
  // the light or dark value for the active theme.
  const accentVars = {
    '--accent-light': accent.light,
    '--accent-dark': accent.dark,
  } as React.CSSProperties;

  return (
    <div className={`accent-field relative overflow-hidden ${className}`} style={accentVars}>
      <div className="accent-rule pointer-events-none absolute inset-2 rounded-md border" aria-hidden="true" />

      <svg
        viewBox="0 0 24 24"
        className={`accent-glyph absolute right-4 top-4 ${size === 'compact' ? 'h-5 w-5' : 'h-8 w-8'}`}
        fill="none"
        stroke="currentColor"
        strokeWidth={1.6}
        aria-hidden="true"
        dangerouslySetInnerHTML={{
          __html: CATEGORY_ICON[story.category] ?? CATEGORY_ICON.Unknown,
        }}
      />

      <div className="absolute inset-x-4 bottom-3 sm:inset-x-5">
        <div
          className="truncate font-serif font-semibold leading-none tracking-tight text-ink-900 dark:text-ink-50"
          style={{ fontSize: subjectSize(subject.length, size) }}
          title={subject}
        >
          {subject}
        </div>

        {kicker && size !== 'compact' && (
          <div className="accent-ink mt-1.5 truncate font-mono text-[10px] font-bold uppercase tracking-[0.12em]">
            {upper(kicker)}
          </div>
        )}
      </div>
    </div>
  );
}
