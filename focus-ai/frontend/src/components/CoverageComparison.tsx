'use client';

import { useState } from 'react';
import { timeAgo } from '@/lib/format';
import type { ComparisonKind, ComparisonPoint, SourceRef } from '@/lib/types';

interface Props {
  sources: SourceRef[];
  points: ComparisonPoint[];
  onSourceClick?: () => void;
}

const KIND_LABEL: Record<ComparisonKind, string> = {
  Shared: 'Ortak',
  Divergent: 'Ayrışıyor',
  Unique: 'Tek kaynakta',
};

const KIND_ORDER: Record<ComparisonKind, number> = { Divergent: 0, Unique: 1, Shared: 2 };

/**
 * What the outlets covering this story actually said — the one thing an aggregator
 * can show that none of them can.
 *
 * Two layers, deliberately: the headline strip and the timeline are built from
 * stored facts and are always there, while the agreement/divergence points come
 * from a model and only appear when each one could be traced back to a verbatim
 * sentence in the outlet it is attributed to. Putting words in a named outlet's
 * mouth is worse than saying nothing, so an unverifiable point is dropped rather
 * than softened.
 */
export function CoverageComparison({ sources, points, onSourceClick }: Props) {
  const [openQuote, setOpenQuote] = useState<number | null>(null);

  if (sources.length === 0) return null;

  const byTime = [...sources].sort(
    (a, b) => new Date(a.publishedAt).getTime() - new Date(b.publishedAt).getTime(),
  );
  const ordered = [...points].sort((a, b) => KIND_ORDER[a.kind] - KIND_ORDER[b.kind]);
  const divergences = points.filter((point) => point.kind === 'Divergent').length;

  return (
    <section className="card p-4 sm:p-5">
      <header className="mb-1 flex flex-wrap items-baseline gap-x-2 gap-y-1">
        <h2 className="text-sm font-semibold text-ink-800 dark:text-ink-100">
          Kaynaklar ne dedi?
        </h2>
        <span className="text-xs text-ink-500 dark:text-ink-400">
          {sources.length === 1 ? 'tek kaynak' : `${sources.length} kaynak`}
          {divergences > 0 && ` · ${divergences} noktada ayrışıyor`}
        </span>
      </header>

      {ordered.length > 0 && (
        <ul className="mb-5 space-y-3">
          {ordered.map((point, index) => (
            <li key={`${point.text}-${index}`} className="text-sm">
              <div className="flex flex-wrap items-center gap-2">
                <span
                  className={`chip text-[11px] ${
                    point.kind === 'Divergent'
                      ? 'bg-signal-caution/15 text-signal-caution'
                      : point.kind === 'Unique'
                        ? 'bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200'
                        : 'bg-ink-100 text-ink-600 dark:bg-ink-800 dark:text-ink-300'
                  }`}
                >
                  {KIND_LABEL[point.kind]}
                </span>
                <span className="text-[11px] text-ink-500 dark:text-ink-400">
                  {point.sources.join(' · ')}
                </span>
              </div>

              <p className="mt-1 leading-relaxed text-ink-700 dark:text-ink-200">{point.text}</p>

              {/* The quote is the receipt. Collapsed by default so the section stays
                  scannable, but never more than one tap away. */}
              <button
                type="button"
                onClick={() => setOpenQuote(openQuote === index ? null : index)}
                aria-expanded={openQuote === index}
                className="mt-1 font-mono text-[11px] text-ink-500 underline decoration-dotted underline-offset-2 hover:text-ink-800 dark:text-ink-400 dark:hover:text-ink-100"
              >
                {openQuote === index ? 'alıntıyı gizle' : 'kaynaktaki cümleyi gör'}
              </button>

              {openQuote === index && (
                <blockquote className="mt-1.5 border-l-2 border-ink-300 pl-3 font-serif text-[13px] italic leading-relaxed text-ink-600 dark:border-ink-700 dark:text-ink-300">
                  “{point.quote}”
                  <footer className="mt-1 font-sans text-[11px] not-italic text-ink-500 dark:text-ink-400">
                    — {point.quoteSource}
                  </footer>
                </blockquote>
              )}
            </li>
          ))}
        </ul>
      )}

      {/* Each outlet's own headline, side by side. No model involved: how a story
          is framed is visible in the words the outlet chose. */}
      <ul className="space-y-2 border-t border-ink-200/70 pt-4 dark:border-ink-800">
        {byTime.map((source) => (
          <li key={`${source.id}-${source.url}`}>
            <a
              href={source.url}
              target="_blank"
              rel="noopener noreferrer"
              onClick={onSourceClick}
              className="flex items-start gap-2 rounded-lg p-2 text-sm transition hover:bg-ink-50 dark:hover:bg-ink-800"
            >
              <span className="mt-0.5 shrink-0" aria-hidden="true">
                {source.isOfficial ? '✅' : '🔗'}
              </span>
              <span className="min-w-0">
                <span className="block text-xs text-ink-500 dark:text-ink-400">
                  {source.name}
                  {source.isOfficial && (
                    <span className="ml-1.5 text-emerald-600 dark:text-emerald-400">resmi</span>
                  )}
                  {' · '}
                  {timeAgo(source.publishedAt)}
                </span>
                <span className="mt-0.5 block font-serif leading-snug text-ink-800 dark:text-ink-100">
                  {source.articleTitle}
                </span>
              </span>
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}
