'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { EmptyState, ErrorState, PageHeader, SignInPrompt } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { api, describeError } from '@/lib/api';
import { trUpper } from '@/lib/format';
import type {
  LearningBrief,
  LearningStory,
  LearningSuggestion,
  StoryCard as Story,
} from '@/lib/types';

const KIND_ICONS: Record<string, string> = {
  docs: '📘',
  video: '🎬',
  repo: '💻',
  article: '📰',
  paper: '📄',
};

interface Bookmark {
  id: string;
  story: Story;
}

/**
 * Öğren: the day's topic on top, everything you saved underneath.
 *
 * The lower half is the point of the page. A bookmark used to be a dead end —
 * the tab suggested one topic a day from your interests and never once looked at
 * what you had actually saved. Now a saved story can become a lesson, and the
 * lesson is a prompt you take to Focus Mentor.
 *
 * Nothing here generates on load. A brief costs a model call, and the reader
 * presses for it.
 */
export default function LearningPage() {
  const { user, loading: authLoading } = useAuth();

  const [today, setToday] = useState<LearningSuggestion | null>(null);
  const [briefs, setBriefs] = useState<LearningBrief[]>([]);
  const [learning, setLearning] = useState<LearningStory[]>([]);
  const [bookmarks, setBookmarks] = useState<Bookmark[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [queueing, setQueueing] = useState<string | null>(null);
  const [queueError, setQueueError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!user) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const [todayResult, briefResult, learningResult, bookmarkResult] = await Promise.all([
        api.learning.today().catch(() => undefined),
        api.learning.briefs.list().catch(() => []),
        api.learning.stories().catch(() => []),
        api.bookmarks.list(1, 50).catch(() => ({ items: [] as Bookmark[] })),
      ]);

      setToday(todayResult ?? null);
      setBriefs(briefResult ?? []);
      setLearning(learningResult ?? []);
      setBookmarks(bookmarkResult.items ?? []);
    } catch {
      setError('Öğrenme sayfası yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }, [user]);

  useEffect(() => {
    if (!authLoading) void load();
  }, [authLoading, load]);

  /** Story ids already in a lesson, so a bookmark is not listed twice. */
  const claimed = useMemo(
    () => new Set(learning.map((entry) => entry.story.id)),
    [learning],
  );

  const unclaimed = bookmarks.filter((bookmark) => !claimed.has(bookmark.story.id));

  const queue = async (run: () => Promise<LearningBrief>, key: string) => {
    setQueueing(key);
    setQueueError(null);

    try {
      const brief = await run();
      // Queueing is idempotent server-side, so a double press must not double the row.
      setBriefs((current) =>
        current.some((existing) => existing.id === brief.id) ? current : [brief, ...current],
      );
    } catch (caught) {
      setQueueError(describeError(caught, 'Öğrenme kuyruğuna eklenemedi.'));
    } finally {
      setQueueing(null);
    }
  };

  const completeToday = async () => {
    if (!today) return;

    const previous = today.status;
    setToday({ ...today, status: 'Completed' });

    try {
      await api.learning.setStatus(today.id, 'Completed');
    } catch {
      setToday({ ...today, status: previous });
    }
  };

  if (!authLoading && !user) {
    return (
      <div className="space-y-4">
        <PageHeader title="Öğren" />
        <SignInPrompt message="Öğrenme önerileri ve kaydettiğin haberler hesabına bağlı, bu yüzden giriş gerekiyor." />
      </div>
    );
  }

  const todayBrief = briefs.find((brief) => brief.origin === 'DailySuggestion' && brief.status !== 'Done');

  return (
    <div className="space-y-4">
      <PageHeader title="Öğren" subtitle="Günün konusu, ve kaydettiklerinden çıkardığın dersler" />

      {error && <ErrorState message={error} onRetry={load} />}

      <div className="space-y-6 px-4 sm:px-5">
        {/* ── Günün önerisi ─────────────────────────────────────────────── */}
        <section className="space-y-2">
          <h2 className="font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
            {trUpper('Günün önerisi')}
          </h2>

          {loading ? (
            <div className="card space-y-3 p-5">
              <div className="skeleton h-4 w-24" />
              <div className="skeleton h-6 w-3/4" />
              <div className="skeleton h-3 w-full" />
            </div>
          ) : today ? (
            <article className="card space-y-3 p-5">
              <div className="flex items-center justify-between">
                <span className="chip bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200">
                  {today.estimatedMinutes} dakika
                </span>
                {today.status === 'Completed' && (
                  <span className="chip bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300">
                    ✓ Tamamlandı
                  </span>
                )}
              </div>

              <h3 className="font-serif text-xl font-semibold text-ink-900 dark:text-ink-50">{today.title}</h3>

              <div>
                <p className="font-mono text-[11px] tracking-[0.13em] text-ink-400">{trUpper('Neden?')}</p>
                <p className="mt-1 font-serif text-sm leading-relaxed text-ink-700 dark:text-ink-300">
                  {today.rationale}
                </p>
              </div>

              {today.resources.length > 0 && (
                <ul className="space-y-1.5 border-t border-ink-100 pt-3 dark:border-ink-800">
                  {today.resources.map((resource) => (
                    <li key={resource.url}>
                      <a
                        href={resource.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="tap-row flex items-center gap-2 rounded-lg p-2 text-sm text-focus-700 transition
                                   hover:bg-ink-50 dark:text-focus-300 dark:hover:bg-ink-800"
                      >
                        <span aria-hidden="true">{KIND_ICONS[resource.kind] ?? '🔗'}</span>
                        <span className="flex-1">{resource.title}</span>
                        <span className="text-xs text-ink-400">{resource.estimatedMinutes} dk</span>
                      </a>
                    </li>
                  ))}
                </ul>
              )}

              <div className="flex flex-col gap-2 sm:flex-row">
                {todayBrief ? (
                  <Link href={`/learning/brief/${todayBrief.id}`} className="btn-primary flex-1">
                    {todayBrief.status === 'Queued' ? 'Sıraya alındı — aç' : 'Promtu aç'}
                  </Link>
                ) : (
                  <button
                    type="button"
                    onClick={() => queue(() => api.learning.briefs.queueDaily(today.id), 'daily')}
                    disabled={queueing !== null}
                    className="btn-primary flex-1"
                  >
                    {queueing === 'daily' ? 'Ekleniyor…' : 'Bunu Mentor ile çalış'}
                  </button>
                )}

                {today.status !== 'Completed' && (
                  <button type="button" onClick={completeToday} className="btn-ghost flex-1">
                    Tamamladım
                  </button>
                )}
              </div>
            </article>
          ) : (
            <EmptyState
              title="Bugün için öneri yok"
              description="Günlük öneri bir yapay zekâ sağlayıcısı yapılandırıldığında üretilir. İlgi alanlarını profilinden güncelleyebilirsin."
            />
          )}
        </section>

        {/*
          ── Çalışacaklarım ──────────────────────────────────────────────

          Feed cards, the same ones Keşfet draws, and they open the story rather
          than the lesson. The lesson is made at the foot of the story now, which
          is the only place the reader has actually read the thing they are about
          to ask to be taught — so this list has no business being a different
          kind of row with a different destination.
        */}
        <section className="space-y-2">
          <h2 className="font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
            {trUpper('Çalışacaklarım')}
          </h2>

          {queueError && <p className="text-xs text-signal-hype">{queueError}</p>}

          {loading ? (
            <StoryCardSkeleton />
          ) : learning.length === 0 ? (
            <EmptyState
              title="Henüz çalışılacak bir şey yok"
              description="Bir haberi aç ve üstteki ✦ düğmesine dokun; buraya düşer, çıktısını haberin altından üretirsin."
            />
          ) : (
            <div className="space-y-3">
              {learning.map((entry) => (
                <StoryCard key={entry.briefId} story={entry.story} />
              ))}
            </div>
          )}
        </section>

        {/* Bookmarks nobody has asked to study yet. Listed rather than
            auto-queued: saving is "read this later", which is not the same as
            "teach me this". */}
        {!loading && unclaimed.length > 0 && (
          <section className="space-y-2">
            <h2 className="font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
              {trUpper('Kaydettiklerim')}
            </h2>

            <div className="space-y-3">
              {unclaimed.map((bookmark) => (
                <StoryCard key={bookmark.id} story={bookmark.story} />
              ))}
            </div>
          </section>
        )}
      </div>
    </div>
  );
}
