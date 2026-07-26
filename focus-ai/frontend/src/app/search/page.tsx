'use client';

import { Suspense, useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '@/components/AuthProvider';
import { EmptyState, ErrorState, PageHeader, SearchBar } from '@/components/Shell';
import { StoryCard, StoryCardSkeleton } from '@/components/StoryCard';
import { api } from '@/lib/api';
import { trUpper } from '@/lib/format';
import type { AskResult, StoryCard as Story } from '@/lib/types';

const EXAMPLES = [
  'Son bir ayda çıkan tüm AI Agent haberleri',
  'Bu hafta .NET ekosisteminde ne değişti?',
  'Angular için önemli breaking change var mı?',
];

function SearchContent() {
  const params = useSearchParams();
  const query = params.get('q') ?? '';
  const { user } = useAuth();

  const [results, setResults] = useState<Story[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [answer, setAnswer] = useState<AskResult | null>(null);
  const [asking, setAsking] = useState(false);

  const run = useCallback(async (value: string) => {
    if (!value.trim()) {
      setResults([]);
      setTotal(0);
      return;
    }

    setLoading(true);
    setError(null);
    setAnswer(null);

    try {
      const result = await api.search(value, 1, 20);
      setResults(result.items);
      setTotal(result.totalCount);
    } catch {
      setError('Arama yapılamadı.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void run(query);
  }, [query, run]);

  const ask = async () => {
    if (!query.trim() || asking) return;

    setAsking(true);
    try {
      setAnswer(await api.ask(query));
    } catch {
      setError('Soru yanıtlanamadı.');
    } finally {
      setAsking(false);
    }
  };

  return (
    <div className="space-y-4">
      <PageHeader
        title="Ara"
        subtitle={query ? `"${query}" için ${total} sonuç` : 'Doğal dille sorabilirsin'}
      />

      <SearchBar initialQuery={query} />

      {!query && (
        <div className="space-y-2 px-4 sm:px-5">
          <p className="text-xs font-semibold tracking-wide text-ink-400">{trUpper('Örnekler')}</p>
          {EXAMPLES.map((example) => (
            <a
              key={example}
              href={`/search?q=${encodeURIComponent(example)}`}
              className="card block p-3 text-sm text-ink-700 dark:text-ink-300"
            >
              {example}
            </a>
          ))}
        </div>
      )}

      {query && user && (
        <div className="px-4 sm:px-5">
          <button type="button" onClick={ask} disabled={asking} className="btn-ghost w-full">
            {asking ? 'Yanıt hazırlanıyor…' : '🤖 Bu soruyu yapay zekâya sor'}
          </button>
        </div>
      )}

      {answer && (
        <section className="card mx-4 space-y-3 p-4 sm:mx-5">
          <h2 className="text-sm font-semibold text-ink-800 dark:text-ink-100">Yapay zekâ yanıtı</h2>
          <p className="whitespace-pre-line text-sm leading-relaxed text-ink-700 dark:text-ink-300">
            {answer.answer}
          </p>
          {answer.citations.length > 0 && (
            <p className="text-xs text-ink-500 dark:text-ink-400">
              {answer.citations.length} habere dayanıyor · güven {Math.round(answer.confidence * 100)}%
            </p>
          )}
        </section>
      )}

      {error && <ErrorState message={error} onRetry={() => run(query)} />}

      <div className="space-y-3 px-4 sm:px-5">
        {loading && (
          <>
            <StoryCardSkeleton />
            <StoryCardSkeleton />
          </>
        )}

        {!loading && query && results.length === 0 && !error && (
          <EmptyState
            title="Sonuç bulunamadı"
            description="Daha genel bir ifade dene ya da farklı bir teknoloji adı yaz."
          />
        )}

        {results.map((story) => (
          <StoryCard key={story.id} story={story} />
        ))}
      </div>
    </div>
  );
}

export default function SearchPage() {
  return (
    <Suspense fallback={<div className="px-4 pt-20 sm:px-5"><StoryCardSkeleton /></div>}>
      <SearchContent />
    </Suspense>
  );
}
