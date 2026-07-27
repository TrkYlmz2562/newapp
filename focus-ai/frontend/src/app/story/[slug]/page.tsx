'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import { useAuth } from '@/components/AuthProvider';
import { ErrorState } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { CommitmentPanel, CorroborationNote } from '@/components/CommitmentBadge';
import { CoverageComparison } from '@/components/CoverageComparison';
import { FeedbackButtons } from '@/components/FeedbackButtons';
import { LearnButton } from '@/components/LearnButton';
import { LearningOutput } from '@/components/LearningOutput';
import { ShareButton } from '@/components/ShareButton';
import { SpeechPlayer } from '@/components/SpeechPlayer';
import { VoiceSetupNotice } from '@/components/VoiceSetupHelp';
import { StoryTimeline } from '@/components/StoryTimeline';
import { StoryVisual } from '@/components/StoryVisual';
import { TrustPanel } from '@/components/TrustBadge';
import { api } from '@/lib/api';
import { forgetOffline, saveForOffline } from '@/lib/offline';
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
  trUpper,
} from '@/lib/format';
import type { StoryDetail, StoryFeedback } from '@/lib/types';

/** Below this the model told us not to trust its own analysis, so we hide it. */
const MIN_ANALYSIS_CONFIDENCE = 0.35;

export default function StoryPage() {
  const params = useParams<{ slug: string }>();
  const { user } = useAuth();
  const [story, setStory] = useState<StoryDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  // Owned here, not by the buttons: the row appears twice and the two copies
  // have to agree. See the note on FeedbackButtons' `value`.
  const [feedback, setFeedback] = useState<StoryFeedback | null>(null);
  // Same reason as the verdict: the action row is drawn twice, so the two copies
  // have to read one answer for "is this story already taken into learning".
  const [learningBriefId, setLearningBriefId] = useState<string | null>(null);
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
        setFeedback(result.feedback ?? null);
        setLearningBriefId(result.learningBriefId ?? null);
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

      // Saving also keeps the story readable with no signal. Best-effort and
      // silent — see lib/offline.
      void (result.saved ? saveForOffline(story.slug) : forgetOffline(story.slug));
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
        <Link
          href="/"
          className="tap-44 -ml-2 inline-flex items-center px-2 py-2.5 text-sm text-ink-500 hover:text-ink-800 dark:text-ink-400"
        >
          ← Geri
        </Link>
        <StoryActions
          story={story}
          canSave={Boolean(user)}
          saved={saved}
          onToggleSave={toggleSave}
          feedback={feedback}
          onFeedback={setFeedback}
          learningBriefId={learningBriefId}
          onLearningQueued={setLearningBriefId}
        />
      </nav>

      {/*
        Play sits directly under the action row, opposite the headline.
        Listening is the alternative to reading this page, so the choice belongs
        where the reader still has it — at the top, before the scroll — and at a
        size that reads as the page's main action rather than a fourth icon.
      */}
      <div className="flex items-start justify-between gap-4">
        <header className="min-w-0 flex-1 space-y-3">
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

        <SpeechPlayer
          sources={[{ slug: story.slug, title: story.title }]}
          variant="round"
          className="flex-none"
        />
      </div>

      <StoryVisual story={story} className="h-56 w-full rounded-2xl sm:h-72" size="hero" />

      {/* The round button renders nothing when the device has no Turkish voice,
          and has no room to say why. This does, at full width. */}
      <VoiceSetupNotice />

      {story.personalNote && (
        <aside className="rounded-2xl border-l-4 border-focus-500 bg-focus-50 p-4 dark:bg-focus-900/30">
          <p className="text-xs font-semibold tracking-wide text-focus-700 dark:text-focus-300">
            {trUpper('Senin stack\'in için')}
          </p>
          <p className="mt-1 text-sm text-focus-900 dark:text-focus-100">{story.personalNote}</p>
        </aside>
      )}

      {summary && (
        <section className="prose-reader">
          <h2 className="mb-2 text-sm font-semibold tracking-wide text-ink-500 dark:text-ink-400">
            {trUpper('Özet')}
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

      {story.commitment ? (
        <CommitmentPanel commitment={story.commitment} />
      ) : (
        story.category === 'Finance' && (
          <CorroborationNote
            sourceCount={story.sources.length}
            hasOfficialSource={story.sources.some((source) => source.isOfficial)}
          />
        )
      )}

      {story.timeline && <StoryTimeline timeline={story.timeline} />}

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

      {/*
        The same row again, where the reading ends.
        A verdict is formed by finishing the piece, not by opening it, and the
        only copy used to be four screens back up — so the reader who decided
        the story was useful had to go and find the button. Same component,
        same state: voting here lights up the row at the top too.
      */}
      <div className="flex items-center justify-end border-t border-ink-200/70 pt-4 dark:border-ink-800">
        <StoryActions
          story={story}
          canSave={Boolean(user)}
          saved={saved}
          onToggleSave={toggleSave}
          feedback={feedback}
          onFeedback={setFeedback}
          learningBriefId={learningBriefId}
          onLearningQueued={setLearningBriefId}
        />
      </div>

      {/* Last, because it is the last decision: what is worth studying about a
          story is something the reader knows once they have finished it. */}
      {user && (
        <LearningOutput
          storyId={story.id}
          briefId={learningBriefId}
          onQueued={setLearningBriefId}
        />
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

/**
 * Verdict, share, save — the three things a reader does to a story.
 *
 * One component because the row is rendered twice, at the head and the foot of
 * the article, and "the same buttons" has to survive someone editing one of
 * them. State is passed in for the same reason: both copies read the page's.
 */
function StoryActions({
  story,
  canSave,
  saved,
  onToggleSave,
  feedback,
  onFeedback,
  learningBriefId,
  onLearningQueued,
}: {
  story: StoryDetail;
  canSave: boolean;
  saved: boolean;
  onToggleSave: () => void;
  feedback: StoryFeedback | null;
  onFeedback: (next: StoryFeedback | null) => void;
  learningBriefId: string | null;
  onLearningQueued: (briefId: string) => void;
}) {
  return (
    <div className="flex items-center gap-2">
      <FeedbackButtons storyId={story.id} surface="detail" value={feedback} onChange={onFeedback} />
      <ShareButton story={story} />
      {canSave && (
        <button
          type="button"
          onClick={onToggleSave}
          aria-pressed={saved}
          aria-label={saved ? 'Kayıtlardan çıkar' : 'Kaydet'}
          title={saved ? 'Kayıtlardan çıkar' : 'Kaydet'}
          className={`tap-44 rounded-lg p-2 transition hover:bg-ink-100 dark:hover:bg-ink-800 ${
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

      {/* Immediately after save, and gated the same way: both write a row against
          the reader, so neither means anything to a signed-out visitor. */}
      {canSave && (
        <LearnButton
          storyId={story.id}
          briefId={learningBriefId}
          onQueued={onLearningQueued}
        />
      )}
    </div>
  );
}

function QaBlock({ title, body }: { title: string; body?: string | null }) {
  if (!body) return null;

  return (
    <section className="card p-4">
      <h3 className="mb-1.5 text-xs font-semibold tracking-wide text-ink-500 dark:text-ink-400">
        {trUpper(title)}
      </h3>
      <p className="text-sm leading-relaxed text-ink-700 dark:text-ink-300">{body}</p>
    </section>
  );
}

function Verdict({ term, value }: { term: string; value: string }) {
  return (
    <div className="rounded-xl bg-ink-50 p-3 dark:bg-ink-800/60">
      <dt className="text-[11px] tracking-wide text-ink-500 dark:text-ink-400">{trUpper(term)}</dt>
      <dd className="mt-0.5 text-sm font-medium text-ink-800 dark:text-ink-100">{value}</dd>
    </div>
  );
}
