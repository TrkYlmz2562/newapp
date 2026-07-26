'use client';

import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { ErrorState } from '@/components/Shell';
import { api, describeError } from '@/lib/api';
import { timeAgo } from '@/lib/format';
import type { SourceHealth, SourceHealthReport, SourceHealthStatus } from '@/lib/types';

const STATUS: Record<SourceHealthStatus, { label: string; chip: string; dot: string }> = {
  Failing: {
    label: 'alınamıyor',
    chip: 'bg-signal-hype/15 text-signal-hype',
    dot: 'bg-signal-hype',
  },
  Stale: {
    label: 'içerik gelmiyor',
    chip: 'bg-signal-caution/15 text-signal-caution',
    dot: 'bg-signal-caution',
  },
  Unknown: {
    label: 'henüz taranmadı',
    chip: 'bg-ink-100 text-ink-500 dark:bg-ink-800 dark:text-ink-400',
    dot: 'bg-ink-400',
  },
  Healthy: {
    label: 'çalışıyor',
    chip: 'bg-signal-trust/15 text-signal-trust',
    dot: 'bg-signal-trust',
  },
  Disabled: {
    label: 'kapalı',
    chip: 'bg-ink-100 text-ink-500 dark:bg-ink-800 dark:text-ink-400',
    dot: 'bg-ink-300 dark:bg-ink-700',
  },
};

const CATEGORY_LABEL: Record<string, string> = {
  OfficialBlog: 'resmi blog',
  Community: 'topluluk',
  Code: 'kod',
  Academic: 'akademik',
  Video: 'video',
  News: 'haber',
};

/** The source's own rhythm, in words. This is what justifies the staleness verdict. */
function cadence(hours: number | null | undefined): string | null {
  if (!hours || hours <= 0) return null;
  if (hours < 48) return `ortalama ${Math.round(hours)} saatte bir yayın`;

  const days = hours / 24;
  if (days < 45) return `ortalama ${Math.round(days)} günde bir yayın`;

  return `ortalama ${Math.round(days / 30)} ayda bir yayın`;
}

export default function SourcesPage() {
  const { user } = useAuth();

  // The button is hidden for non-admins as a courtesy; the endpoint is what
  // actually enforces it, via the "admin" policy.
  const isAdmin = user?.roles?.includes('admin') ?? false;

  const [report, setReport] = useState<SourceHealthReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setReport(await api.sourceHealth());
      setError(null);
    } catch (caught) {
      setError(describeError(caught, 'Kaynaklar yüklenemedi.'));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Optimistic, and reverted on failure — the round trip is visible on a phone.
  const patch = (id: string, change: Partial<SourceHealth>) =>
    setReport((current) =>
      current
        ? { ...current, sources: current.sources.map((s) => (s.id === id ? { ...s, ...change } : s)) }
        : current,
    );

  const toggleFavorite = async (source: SourceHealth) => {
    if (!user || busy) return;

    const next = !source.isFavorite;
    setBusy(source.id);
    patch(source.id, { isFavorite: next });

    try {
      const result = await api.profile.toggleFavoriteSource(source.id);
      patch(source.id, { isFavorite: result.favorite });
    } catch {
      patch(source.id, { isFavorite: !next });
    } finally {
      setBusy(null);
    }
  };

  const toggleEnabled = async (source: SourceHealth) => {
    if (!isAdmin || busy) return;

    const next = !source.isEnabled;
    setBusy(source.id);

    try {
      await api.admin.setSourceEnabled(source.id, next);
      // Reloaded rather than patched: the status verdict is computed server-side
      // and switching a source on also clears its back-off, so a local guess at
      // the new state would be wrong.
      await load();
    } catch (caught) {
      setError(describeError(caught, 'Kaynak durumu değiştirilemedi.'));
    } finally {
      setBusy(null);
    }
  };

  if (error && !report) {
    return (
      <div className="pt-6">
        <ErrorState message={error} />
      </div>
    );
  }

  if (!report) {
    return (
      <div className="space-y-3 px-4 pt-6 sm:px-5">
        <div className="skeleton h-6 w-40" />
        <div className="skeleton h-16 w-full" />
        <div className="skeleton h-16 w-full" />
        <div className="skeleton h-16 w-full" />
      </div>
    );
  }

  const problems = report.failingCount + report.staleCount;

  return (
    <div className="space-y-5 px-4 pt-6 sm:px-5">
      <header className="space-y-2">
        <Link href="/profile" className="text-sm text-ink-500 hover:text-ink-800 dark:text-ink-400">
          ← Profil
        </Link>
        <h1 className="font-serif text-2xl font-semibold text-ink-900 dark:text-ink-50">Kaynaklar</h1>
        <p className="text-sm leading-relaxed text-ink-600 dark:text-ink-300">
          Bir akış HTTP 200 dönüp aylardır güncellenmemiş içerik servis edebilir — tarama
          başarılı görünür, kaynak yeşil kalır ve akışına hiçbir şey gelmez. Buradaki
          değerlendirme sessizliği, kaynağın <strong>kendi yayın ritmine</strong> göre
          ölçüyor: ayda bir yazan bir blog 10 günde bayat değil, saatte bir yazan bir
          ajans öyle.
        </p>
      </header>

      <div className="flex flex-wrap gap-2 font-mono text-[11px]">
        {(
          [
            ['Failing', report.failingCount],
            ['Stale', report.staleCount],
            ['Healthy', report.healthyCount],
            ['Disabled', report.disabledCount],
          ] as const
        )
          .filter(([, count]) => count > 0)
          .map(([status, count]) => (
            <span key={status} className={`chip ${STATUS[status].chip}`}>
              {count} {STATUS[status].label}
            </span>
          ))}
      </div>

      {problems === 0 && (
        <p className="rounded-lg border-l-2 border-signal-trust bg-signal-trust/5 p-3 text-sm text-ink-700 dark:text-ink-200">
          Tüm kaynaklar çalışıyor ve içerik üretiyor.
        </p>
      )}

      {error && (
        <p className="rounded-lg bg-signal-hype/10 p-3 text-sm text-signal-hype">{error}</p>
      )}

      <ul className="space-y-2">
        {report.sources.map((source) => {
          const tone = STATUS[source.status];
          const rhythm = cadence(source.typicalGapHours);

          return (
            <li key={source.id} className="card p-3.5 sm:p-4">
              <div className="flex items-start gap-2.5">
                <span
                  className={`mt-1.5 h-2 w-2 flex-none rounded-full ${tone.dot}`}
                  aria-hidden="true"
                />

                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                    <a
                      href={source.websiteUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="font-serif font-semibold text-ink-900 hover:underline dark:text-ink-50"
                    >
                      {source.name}
                    </a>
                    <span className={`chip text-[10.5px] ${tone.chip}`}>{tone.label}</span>
                    {source.isOfficial && (
                      <span className="font-mono text-[10.5px] text-emerald-600 dark:text-emerald-400">
                        resmi
                      </span>
                    )}
                  </div>

                  {/* The sentence, not just the colour: a status light is not a claim
                      the reader can check. */}
                  <p className="mt-1 text-[13px] leading-relaxed text-ink-600 dark:text-ink-300">
                    {source.reason}
                  </p>

                  <div className="mt-1.5 flex flex-wrap items-center gap-x-2.5 gap-y-1 font-mono text-[11px] text-ink-500 dark:text-ink-400">
                    <span>{CATEGORY_LABEL[source.category] ?? source.category}</span>
                    <span>·</span>
                    <span>{source.language === 'tr' ? 'Türkçe' : source.language.toUpperCase()}</span>
                    <span>·</span>
                    <span>30 günde {source.articleCount30d} haber</span>
                    {rhythm && (
                      <>
                        <span>·</span>
                        <span>{rhythm}</span>
                      </>
                    )}
                    {source.lastSucceededAt && (
                      <>
                        <span>·</span>
                        <span>son tarama {timeAgo(source.lastSucceededAt)}</span>
                      </>
                    )}
                  </div>
                </div>

                <div className="flex flex-none items-center gap-1">
                  {user && (
                    <button
                      type="button"
                      onClick={() => toggleFavorite(source)}
                      disabled={busy === source.id}
                      aria-pressed={source.isFavorite}
                      aria-label={
                        source.isFavorite
                          ? `${source.name} takibi bırak`
                          : `${source.name} kaynağını takip et`
                      }
                      title={source.isFavorite ? 'Takibi bırak' : 'Takip et'}
                      className={`tap-row rounded-lg p-1.5 transition hover:bg-ink-100 disabled:opacity-50 dark:hover:bg-ink-800 ${
                        source.isFavorite ? 'text-focus-600 dark:text-focus-400' : 'text-ink-400 dark:text-ink-500'
                      }`}
                    >
                      <svg
                        className="h-4 w-4"
                        viewBox="0 0 24 24"
                        fill={source.isFavorite ? 'currentColor' : 'none'}
                        stroke="currentColor"
                        strokeWidth={1.8}
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        aria-hidden="true"
                      >
                        <path d="m12 3.8 2.5 5.1 5.6.8-4 3.9 1 5.6-5.1-2.7-5.1 2.7 1-5.6-4-3.9 5.6-.8z" />
                      </svg>
                    </button>
                  )}

                  {isAdmin && (
                    <button
                      type="button"
                      onClick={() => toggleEnabled(source)}
                      disabled={busy === source.id}
                      aria-pressed={!source.isEnabled}
                      aria-label={
                        source.isEnabled ? `${source.name} kaynağını kapat` : `${source.name} kaynağını aç`
                      }
                      title={source.isEnabled ? 'Kaynağı kapat' : 'Kaynağı aç'}
                      className="tap-row rounded-lg p-1.5 text-ink-400 transition hover:bg-ink-100 disabled:opacity-50 dark:text-ink-500 dark:hover:bg-ink-800"
                    >
                      <svg
                        className="h-4 w-4"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth={1.8}
                        strokeLinecap="round"
                        aria-hidden="true"
                      >
                        {source.isEnabled ? (
                          <>
                            <path d="M12 4v8" />
                            <path d="M6.3 7.3a8 8 0 1 0 11.4 0" />
                          </>
                        ) : (
                          <>
                            <circle cx="12" cy="12" r="8.5" />
                            <path d="M12 8v8" />
                          </>
                        )}
                      </svg>
                    </button>
                  )}
                </div>
              </div>
            </li>
          );
        })}
      </ul>

      {!user && (
        <p className="text-xs text-ink-500 dark:text-ink-400">
          Kaynak takip etmek için{' '}
          <Link href="/login" className="underline">
            giriş yap
          </Link>
          . Takip ettiğin kaynaklardan gelen haberler akışında biraz yukarı çıkar.
        </p>
      )}
    </div>
  );
}
