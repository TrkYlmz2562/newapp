'use client';

import { useEffect, useRef, useState } from 'react';
import { CATEGORY_ART, CATEGORY_ICON, CATEGORY_LABELS } from '@/lib/format';
import { drawCircuit, seededRandom } from '@/lib/illustration';
import type { ContentCategory } from '@/lib/types';

interface VisualStory {
  heroImageUrl?: string | null;
  category: ContentCategory;
  slug: string;
}

interface Props {
  story: VisualStory;
  className?: string;
  /** The monospace category tag in the corner. Hidden where the category is already shown. */
  showTag?: boolean;
}

/**
 * The story's visual: the real publisher/OG image when we have one, otherwise a
 * generated circuit illustration painted on a category-coloured gradient. A story
 * without an image is never hidden or penalised — it just shows the illustration.
 * A remote image that 404s or is blocked falls back to the same illustration via
 * onError rather than leaving a broken-image icon.
 */
export function StoryVisual({ story, className = '', showTag = true }: Props) {
  const [broken, setBroken] = useState(false);
  const useImage = Boolean(story.heroImageUrl) && !broken;

  const boxRef = useRef<HTMLDivElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    if (useImage) return undefined;
    const box = boxRef.current;
    const canvas = canvasRef.current;
    if (!box || !canvas) return undefined;
    const ctx = canvas.getContext('2d');
    if (!ctx) return undefined;

    const draw = () => {
      const { width, height } = box.getBoundingClientRect();
      if (width < 2 || height < 2) return;
      const dpr = Math.min(2, window.devicePixelRatio || 1);
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, width, height);
      drawCircuit(ctx, width, height, seededRandom(story.slug || 'focus'));
    };

    draw();
    const observer = new ResizeObserver(draw);
    observer.observe(box);
    return () => observer.disconnect();
  }, [useImage, story.slug]);

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

  const [from, to] = CATEGORY_ART[story.category] ?? CATEGORY_ART.Unknown;

  return (
    <div
      ref={boxRef}
      className={`relative overflow-hidden ${className}`}
      style={{ backgroundImage: `linear-gradient(150deg, ${from}, ${to})` }}
    >
      <canvas ref={canvasRef} className="absolute inset-0 h-full w-full" aria-hidden="true" />
      {showTag && (
        <span
          className="absolute bottom-2.5 left-3 inline-flex items-center gap-1.5 font-mono text-[10.5px] font-bold uppercase tracking-[0.13em] text-white"
          style={{ textShadow: '0 1px 3px rgba(0,0,0,.55)' }}
        >
          <svg
            viewBox="0 0 24 24"
            className="h-[15px] w-[15px]"
            fill="none"
            stroke="currentColor"
            strokeWidth={1.7}
            aria-hidden="true"
            dangerouslySetInnerHTML={{
              __html: CATEGORY_ICON[story.category] ?? CATEGORY_ICON.Unknown,
            }}
          />
          {CATEGORY_LABELS[story.category]}
        </span>
      )}
    </div>
  );
}
