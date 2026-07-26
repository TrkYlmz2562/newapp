'use client';

import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { EmptyState, ErrorState, PageHeader } from '@/components/Shell';
import { api } from '@/lib/api';
import { formatPercent } from '@/lib/format';
import type { DigestPeriod, Trend } from '@/lib/types';

export default function TrendsPage() {
  const [period, setPeriod] = useState<DigestPeriod>('Monthly');
  const [trends, setTrends] = useState<Trend[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      setTrends(await api.trends(period, 20));
    } catch {
      setError('Trendler yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }, [period]);

  useEffect(() => {
    void load();
  }, [load]);

  // Bars are relative to the busiest topic, so the chart reads at any scale.
  const peak = Math.max(1, ...trends.map((trend) => trend.storyCount));

  return (
    <div className="space-y-4">
      <PageHeader title="Trendler" subtitle="En çok konuşulan teknolojiler" />

      <div role="tablist" aria-label="Dönem" className="flex gap-2 px-4 sm:px-5">
        {(['Weekly', 'Monthly'] as const).map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            aria-selected={period === value}
            onClick={() => setPeriod(value)}
            className={`chip border px-3 py-1.5 text-sm ${
              period === value
                ? 'border-focus-500 bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200'
                : 'border-ink-300 bg-transparent text-ink-700 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200'
            }`}
          >
            {value === 'Weekly' ? 'Bu hafta' : 'Bu ay'}
          </button>
        ))}
      </div>

      {error && <ErrorState message={error} onRetry={load} />}

      <div className="px-4 sm:px-5">
        {loading ? (
          <div className="card space-y-3 p-5">
            {Array.from({ length: 6 }).map((_, index) => (
              <div key={index} className="skeleton h-6 w-full" />
            ))}
          </div>
        ) : trends.length === 0 ? (
          <EmptyState
            title="Henüz trend verisi yok"
            description="Trendler yeterli haber biriktiğinde her gece hesaplanır."
          />
        ) : (
          <ol className="card divide-y divide-ink-100 dark:divide-ink-800">
            {trends.map((trend, index) => (
              <li key={trend.topicId}>
                <Link
                  href={`/explore?topic=${trend.slug}`}
                  className="flex items-center gap-3 p-3 transition hover:bg-ink-50 dark:hover:bg-ink-800/60"
                >
                  <span className="w-5 shrink-0 text-right text-xs tabular-nums text-ink-400">
                    {index + 1}
                  </span>

                  <span className="min-w-0 flex-1">
                    <span className="flex items-center justify-between gap-2">
                      <span className="truncate text-sm font-medium text-ink-800 dark:text-ink-100">
                        {trend.name}
                      </span>
                      <span
                        className={`shrink-0 text-xs font-medium tabular-nums ${
                          trend.momentumPercent >= 0
                            ? 'text-emerald-600 dark:text-emerald-400'
                            : 'text-rose-600 dark:text-rose-400'
                        }`}
                      >
                        {formatPercent(trend.momentumPercent)}
                      </span>
                    </span>

                    <span
                      className="mt-1.5 block h-1.5 overflow-hidden rounded-full bg-ink-100 dark:bg-ink-800"
                      role="img"
                      aria-label={`${trend.storyCount} haber`}
                    >
                      <span
                        className="block h-full rounded-full bg-focus-500"
                        style={{ width: `${Math.max(4, (trend.storyCount / peak) * 100)}%` }}
                      />
                    </span>

                    <span className="mt-1 block text-xs text-ink-500 dark:text-ink-400">
                      {trend.storyCount} haber · {trend.sourceCount} kaynak
                    </span>
                  </span>
                </Link>
              </li>
            ))}
          </ol>
        )}
      </div>
    </div>
  );
}
