'use client';

import Link from 'next/link';
import { useState } from 'react';
import { api, describeError } from '@/lib/api';
import { trUpper } from '@/lib/format';
import type { LearningBrief } from '@/lib/types';

/**
 * One line in the Öğren list: something you saved, and how far it has got
 * towards being a lesson.
 *
 * The three states are deliberately visible rather than collapsed into a single
 * "open" affordance, because they cost different things. Queued has spent
 * nothing and says so; generating is the one press in this whole feature that
 * calls a model; opening a generated brief is free forever after.
 */
export function BriefRow({
  brief,
  onChanged,
}: {
  brief: LearningBrief;
  onChanged: (next: LearningBrief | null) => void;
}) {
  const [busy, setBusy] = useState<'generate' | 'delete' | null>(null);
  const [error, setError] = useState<string | null>(null);

  const generate = async () => {
    setBusy('generate');
    setError(null);

    try {
      onChanged(await api.learning.briefs.generate(brief.id));
    } catch (caught) {
      setError(describeError(caught, 'Prompt üretilemedi.'));
    } finally {
      setBusy(null);
    }
  };

  const remove = async () => {
    setBusy('delete');

    try {
      await api.learning.briefs.remove(brief.id);
      onChanged(null);
    } catch (caught) {
      setError(describeError(caught, 'Silinemedi.'));
      setBusy(null);
    }
  };

  const done = brief.status === 'Done';
  const ready = brief.status !== 'Queued';

  return (
    <article
      className={`card p-4 transition ${done ? 'opacity-60' : ''}`}
      aria-busy={busy !== null}
    >
      <div className="flex items-start gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1 font-mono text-[11px] text-ink-500 dark:text-ink-400">
            {done ? (
              <span className="text-signal-trust">{trUpper('✓ çalışıldı')}</span>
            ) : ready ? (
              <span className="text-focus-600 dark:text-focus-400">{trUpper('hazır')}</span>
            ) : (
              <span>{trUpper('sırada')}</span>
            )}

            {brief.entryLabel && <span>· {brief.entryLabel}</span>}

            {brief.stories.length > 1 && <span>· {brief.stories.length} haber</span>}

            {brief.origin === 'DailySuggestion' && <span>· günün konusu</span>}
          </div>

          <h3 className="mt-1.5 font-serif text-base font-semibold leading-snug text-ink-900 dark:text-ink-50">
            {brief.title}
          </h3>

          {brief.learningGoal && (
            <p className="mt-1 font-serif text-[13.5px] leading-relaxed text-ink-600 dark:text-ink-300">
              {brief.learningGoal}
            </p>
          )}

          {brief.demoIdea && (
            <p className="mt-1.5 text-[12px] text-ink-500 dark:text-ink-400">
              <span className="font-mono text-[11px] text-focus-600 dark:text-focus-400">
                {trUpper('demo')}{' '}
              </span>
              {brief.demoIdea}
            </p>
          )}

          {/* Said out loud: the brief is thinner than usual and the reader should
              know why before they take it into a lesson. */}
          {brief.plannerUnavailable && ready && (
            <p className="mt-1.5 text-[12px] text-signal-caution">
              Çapa sorular üretilemedi — dosya yine de hazır.
            </p>
          )}
        </div>

        <button
          type="button"
          onClick={remove}
          disabled={busy !== null}
          aria-label="Bu dersi listeden kaldır"
          className="tap-row -m-1.5 rounded-lg p-1.5 text-ink-400 transition hover:bg-ink-100
                     hover:text-ink-700 disabled:opacity-40 dark:hover:bg-ink-800 dark:hover:text-ink-200"
        >
          <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden="true">
            <path strokeLinecap="round" d="M6 6l12 12M18 6L6 18" />
          </svg>
        </button>
      </div>

      {error && <p className="mt-2 text-xs text-signal-hype">{error}</p>}

      <div className="mt-3">
        {ready ? (
          <Link href={`/learning/brief/${brief.id}`} className="btn-ghost w-full">
            {done ? 'Promtu tekrar aç' : 'Promtu aç ve kopyala'}
          </Link>
        ) : (
          <button
            type="button"
            onClick={generate}
            disabled={busy !== null}
            className="btn-primary w-full"
          >
            {busy === 'generate' ? 'Üretiliyor…' : 'Prompt üret'}
          </button>
        )}
      </div>
    </article>
  );
}
