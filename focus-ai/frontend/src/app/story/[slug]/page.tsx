'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import { useAuth } from '@/components/AuthProvider';
import { ErrorState } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { CoverageComparison } from '@/components/CoverageComparison';
import { ShareButton } from '@/components/ShareButton';
import { StoryVisual } from '@/components/StoryVisual';
import { TrustPanel } from '@/components/TrustBadge';
import { api } from '@/lib/api';
import {
  CATEGORY_EMOJI,
  CATEGORY_LABELS,
  HYPE_LABELS,
  LONGEVITY_LABELS,
  URGENCY_LABELS,
  formatDate,
  meaningful,
  readingTime,
  timeAgo,
} from '@/lib/format';
import type { StoryDetail } from '@/lib/types';

/** Below this the model told us not to trust its own analysis, so we hide it. */
const MIN_ANALYSIS_CONFIDENCE = 0.35;

export default function StoryPage() {
  const params = useParams<{ slug: string }>();
  const { user } = useAuth();
  const [story, setStory] = useState<StoryDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const openedAt = useRef<number>(Date.now());

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setLoading(true);
      setError(null);

      try {
        const result = await api.stories.detail(params.slug);
        if (cancelled) return;

        setStory(result);
        setSaved(result.isBookmarked);
        openedAt.current = Date.now();

        if (user) {
          void api.stories.recordInteraction(result.id, 'Open', undefined, 'detail').catch(() => {});
        }
      } catch {
        if (!cancelled) setError('Haber yüklenemedi.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [params.slug, user]);

  // Dwell time on unmount feeds both personalisation and the KPI set. Reported
  // only past a threshold, since a bounce is not a read.
  useEffect(() => {
    return () => {
      if (!story || !user) return;

      const seconds = Math.round((Date.now() - openedAt.current) / 1000);
      if (seconds < 10) return;

      void api.stories
        .recordInteraction(story.id, 'ReadComplete', Math.min(seconds, 3600), 'detail')
        .catch(() => {});
    };
  }, [story, user]);

  const toggleSave = async () => {
    if (!story || !user) return;

    const next = !saved;
    setSaved(next);

    try {
      const result = await api.bookmarks.toggle(story.id);
      setSaved(result.saved);
    } catch {
      setSaved(!next);
    }
  };

  if (loading) {
    return (
      <div className="space-y-3 px-4 pt-6 sm:px-5">
        <div className="skeleton h-5 w-32" />
        <div className="skeleton h-8 w-full" />
        <div className="skeleton h-8 w-3/4" />
        <StoryCardSkeleton />
      </div>
    );
  }

  if (error || !story) {
    return (
      <div className="pt-6">
        <ErrorState message={error ?? 'Haber bulunamadı.'} />
        <div className="mt-4 px-4 sm:px-5">
          <Link href="/" className="btn-ghost">
            Ana sayfaya dön
          </Link>
        </div>
      </div>
    );
  }

  const showAnalysis = story.analysis && story.analysis.confidence >= MIN_ANALYSIS_CONFIDENCE;

  // Hide the standfirst and summary when they merely restate the headline —
  // which is exactly what the extractive fallback produces for body-less feeds.
  const dek = meaningful(story.dek, story.title);
  const summary = meaningful(story.summary, story.title);

  return (
    <article className="space-y-5 px-4 pt-6 sm:px-5">
      <nav className="flex items-center justify-between">
        <Link href="/" className="text-sm text-ink-500 hover:text-ink-800 dark:text-ink-400">
          ← Geri
        </Link>
        <div className="flex items-center gap-2">
          <ShareButton story={story} />
          {user && (
            <button
              type="button"
              onClick={toggleSave}
              aria-pressed={saved}
              aria-label={saved ? 'Kayıtlardan çıkar' : 'Kaydet'}
              title={saved ? 'Kayıtlardan çıkar' : 'Kaydet'}
              className={`rounded-lg p-2 transition hover:bg-ink-100 dark:hover:bg-ink-800 ${
                saved ? 'text-focus-600 dark:text-focus-400' : 'text-ink-500 dark:text-ink-400'
              }`}
            >
              <svg
                className="h-5 w-5"
                viewBox="0 0 24 24"
                fill={saved ? 'currentColor' : 'none'}
                stroke="currentColor"
                strokeWidth={1.8}
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden="true"
              >
                <path d="M6 4h12v17l-6-4-6 4z" />
              </svg>
            </button>
          )}
        </div>
      </nav>

      <header className="space-y-3">
        <div className="flex flex-wrap items-center gap-2 text-xs text-ink-500 dark:text-ink-400">
          <span className="chip bg-ink-100 text-ink-600 dark:bg-ink-800 dark:text-ink-300">
            {CATEGORY_EMOJI[story.category]} {CATEGORY_LABELS[story.category]}
          </span>
          <time dateTime={story.publishedAt}>{formatDate(story.publishedAt)}</time>
          <span>·</span>
          <span>{readingTime(story.readingMinutes)}</span>
        </div>

        <h1 className="font-serif text-2xl font-semibold leading-tight tracking-tight text-ink-900 dark:text-ink-50 sm:text-3xl">
          {story.title}
        </h1>

        {dek && <p className="text-base text-ink-600 dark:text-ink-300">{dek}</p>}
      </header>

      <StoryVisual story={story} className="h-56 w-full rounded-2xl sm:h-72" size="hero" />

      {story.personalNote && (
        <aside className="rounded-2xl border-l-4 border-focus-500 bg-focus-50 p-4 dark:bg-focus-900/30">
          <p className="text-xs font-semibold uppercase tracking-wide text-focus-700 dark:text-focus-300">
            Senin stack&apos;in için
          </p>
          <p className="mt-1 text-sm text-focus-900 dark:text-focus-100">{story.personalNote}</p>
        </aside>
      )}

      {summary && (
        <section className="prose-reader">
          <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400">
            Özet
          </h2>
          <p>{summary}</p>
        </section>
      )}

      {story.keyPoints.length > 0 && (
        <section className="card p-4">
          <h2 className="mb-2 text-sm font-semibold text-ink-800 dark:text-ink-100">Öne çıkanlar</h2>
          <ul className="space-y-1.5">
            {story.keyPoints.map((point) => (
              <li key={point} className="flex gap-2 text-sm text-ink-700 dark:text-ink-300">
                <span aria-hidden="true" className="text-focus-500">
                  •
                </span>
                {point}
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="grid gap-3 sm:grid-cols-3">
        <QaBlock title="Neden önemli?" body={story.whyItMatters} />
        <QaBlock title="Kimleri etkiliyor?" body={story.whoIsAffected} />
        <QaBlock title="Ben ne yapmalıyım?" body={story.whatShouldIDo} />
      </div>

      {story.trust && <TrustPanel trust={story.trust} />}

      {showAnalysis && story.analysis && (
        <section className="card space-y-3 p-4">
          <h2 className="text-sm font-semibold text-ink-800 dark:text-ink-100">🤖 AI Yorumu</h2>
          <p className="text-sm leading-relaxed text-ink-700 dark:text-ink-300">
            {story.analysis.whyImportant}
          </p>

          {story.analysis.realImpact && (
            <p className="text-sm leading-relaxed text-ink-700 dark:text-ink-300">
              <span className="font-semibold">Gerçek etkisi: </span>
              {story.analysis.realImpact}
            </p>
          )}

          <dl className="grid gap-2 sm:grid-cols-3">
            <Verdict term="Abartılıyor mu?" value={HYPE_LABELS[story.analysis.hype]} />
            <Verdict term="Ne zaman öğrenmeli?" value={URGENCY_LABELS[story.analysis.learnUrgency]} />
            <Verdict term="Kalıcı mı?" value={LONGEVITY_LABELS[story.analysis.longevity]} />
          </dl>

          {story.analysis.hypeReasoning && (
            <p className="text-xs text-ink-500 dark:text-ink-400">{story.analysis.hypeReasoning}</p>
          )}
        </section>
      )}

      <CoverageComparison
        sources={story.sources}
        points={story.comparison}
        onSourceClick={() => {
          if (user) void api.stories.recordInteraction(story.id, 'SourceClick', undefined, 'detail').catch(() => {});
        }}
      />

      {story.links.length > 0 && (
        <section className="card p-4">
          <h2 className="mb-3 text-sm font-semibold text-ink-800 dark:text-ink-100">İlgili materyaller</h2>
          <ul className="space-y-2">
            {story.links.map((link) => (
              <li key={link.url}>
                <a
                  href={link.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="block rounded-lg p-2 text-sm text-focus-700 transition hover:bg-ink-50 dark:text-focus-300 dark:hover:bg-ink-800"
                >
                  {link.kind === 'Video' ? '🎬' : link.kind === 'GitHub' ? '💻' : link.kind === 'Paper' ? '📄' : '🔗'}{' '}
                  {link.title}
                </a>
              </li>
            ))}
          </ul>
        </section>
      )}

      {story.related.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-semibold text-ink-500 dark:text-ink-400">İlgili Haberler</h2>
          {story.related.map((related) => (
            <StoryCard key={related.id} story={related} variant="compact" />
          ))}
        </section>
      )}
    </article>
  );
}

function QaBlock({ title, body }: { title: string; body?: string | null }) {
  if (!body) return null;

  return (
    <section className="card p-4">
      <h3 className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400">
        {title}
      </h3>
      <p className="text-sm leading-relaxed text-ink-700 dark:text-ink-300">{body}</p>
    </section>
  );
}

function Verdict({ term, value }: { term: string; value: string }) {
  return (
    <div className="rounded-xl bg-ink-50 p-3 dark:bg-ink-800/60">
      <dt className="text-[11px] uppercase tracking-wide text-ink-500 dark:text-ink-400">{term}</dt>
      <dd className="mt-0.5 text-sm font-medium text-ink-800 dark:text-ink-100">{value}</dd>
    </div>
  );
}
