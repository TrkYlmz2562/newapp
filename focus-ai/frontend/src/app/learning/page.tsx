'use client';

import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { EmptyState, ErrorState, PageHeader, SignInPrompt } from '@/components/Shell';
import { api } from '@/lib/api';
import { formatDate, trUpper } from '@/lib/format';
import type { LearningSuggestion } from '@/lib/types';

const KIND_ICONS: Record<string, string> = {
  docs: '📘',
  video: '🎬',
  repo: '💻',
  article: '📰',
  paper: '📄',
};

export default function LearningPage() {
  const { user, loading: authLoading } = useAuth();
  const [today, setToday] = useState<LearningSuggestion | null>(null);
  const [history, setHistory] = useState<LearningSuggestion[]>([]);
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
      const [todayResult, historyResult] = await Promise.all([
        api.learning.today().catch(() => undefined),
        api.learning.history(20).catch(() => []),
      ]);

      setToday(todayResult ?? null);
      // Today's item already has its own card; showing it twice is noise.
      setHistory((historyResult ?? []).filter((item) => item.id !== todayResult?.id));
    } catch {
      setError('Öğrenme önerileri yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }, [user]);

  useEffect(() => {
    if (!authLoading) void load();
  }, [authLoading, load]);

  const complete = async () => {
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
        <SignInPrompt message="Günlük öğrenme önerileri ilgi alanlarına göre hazırlanır, bu yüzden giriş gerekiyor." />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <PageHeader title="Öğren" subtitle="Her gün bir konu, senin gündemine göre" />

      {error && <ErrorState message={error} onRetry={load} />}

      <div className="space-y-4 px-4 sm:px-5">
        {loading ? (
          <div className="card space-y-3 p-5">
            <div className="skeleton h-4 w-24" />
            <div className="skeleton h-6 w-3/4" />
            <div className="skeleton h-3 w-full" />
          </div>
        ) : today ? (
          <section className="card space-y-3 p-5">
            <div className="flex items-center justify-between">
              <span className="chip bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200">
                Bugün · {today.estimatedMinutes} dakika
              </span>
              {today.status === 'Completed' && (
                <span className="chip bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300">
                  ✓ Tamamlandı
                </span>
              )}
            </div>

            <h2 className="text-xl font-bold text-ink-900 dark:text-ink-50">{today.title}</h2>

            <div>
              <p className="text-xs font-semibold tracking-wide text-ink-400">{trUpper('Neden?')}</p>
              <p className="mt-1 text-sm leading-relaxed text-ink-700 dark:text-ink-300">
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
                      className="flex items-center gap-2 rounded-lg p-2 text-sm text-focus-700 transition
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

            {today.status !== 'Completed' && (
              <button type="button" onClick={complete} className="btn-primary w-full">
                Tamamladım
              </button>
            )}
          </section>
        ) : (
          <EmptyState
            title="Bugün için öneri yok"
            description="Öğrenme önerileri bir yapay zekâ sağlayıcısı yapılandırıldığında üretilir. İlgi alanlarını profilinden güncelleyebilirsin."
          />
        )}

        {history.length > 0 && (
          <section className="space-y-2">
            <h2 className="text-sm font-semibold text-ink-500 dark:text-ink-400">Geçmiş</h2>
            {history.map((item) => (
              <div key={item.id} className="card flex items-center gap-3 p-3">
                <span aria-hidden="true">{item.status === 'Completed' ? '✅' : '○'}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-ink-800 dark:text-ink-100">
                    {item.title}
                  </p>
                  <p className="text-xs text-ink-500 dark:text-ink-400">{formatDate(item.date)}</p>
                </div>
              </div>
            ))}
          </section>
        )}
      </div>
    </div>
  );
}
