'use client';

import { useState } from 'react';
import { trustBand } from '@/lib/format';
import type { Trust } from '@/lib/types';

const TONE_CLASSES = {
  good: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300',
  ok: 'bg-amber-50 text-amber-700 dark:bg-amber-950/60 dark:text-amber-300',
  warn: 'bg-rose-50 text-rose-700 dark:bg-rose-950/60 dark:text-rose-300',
} as const;

export function TrustChip({ score }: { score: number }) {
  const { label, tone } = trustBand(score);

  return (
    <span className={`chip ${TONE_CLASSES[tone]}`} title={`Güven skoru: ${score}/100`}>
      <ShieldIcon />
      {score}
      <span className="sr-only">{` — ${label}`}</span>
    </span>
  );
}

const CRITERIA = [
  { key: 'officialSourceScore', label: 'Resmi kaynak' },
  { key: 'corroborationScore', label: 'Kaç kaynak doğruladı' },
  { key: 'technicalAccuracyScore', label: 'Teknik doğruluk' },
  { key: 'communityScore', label: 'Topluluk güveni' },
  { key: 'recencyScore', label: 'Güncellik' },
] as const;

/**
 * The full breakdown. Showing only a number would be false precision — the whole
 * value of a trust score is being able to see why it says what it says.
 */
export function TrustPanel({ trust }: { trust: Trust }) {
  const [open, setOpen] = useState(false);
  const { label, tone } = trustBand(trust.total);

  return (
    <section className="card p-4">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        className="flex w-full items-center justify-between gap-3 text-left"
      >
        <span className="flex items-center gap-3">
          <span className={`grid h-11 w-11 place-items-center rounded-xl text-base font-bold ${TONE_CLASSES[tone]}`}>
            {trust.total}
          </span>
          <span>
            <span className="block text-sm font-semibold">{label}</span>
            <span className="block text-xs text-ink-500 dark:text-ink-400">
              {trust.explanation ?? 'Güven skoru ayrıntıları'}
            </span>
          </span>
        </span>
        <ChevronIcon open={open} />
      </button>

      {open && (
        <dl className="mt-4 space-y-2.5 border-t border-ink-100 pt-4 dark:border-ink-800">
          {CRITERIA.map(({ key, label: criterion }) => (
            <div key={key} className="flex items-center gap-3">
              <dt className="w-40 shrink-0 text-xs text-ink-500 dark:text-ink-400">{criterion}</dt>
              <dd className="flex flex-1 items-center gap-2">
                <div
                  className="h-1.5 flex-1 overflow-hidden rounded-full bg-ink-100 dark:bg-ink-800"
                  role="img"
                  aria-label={`${criterion}: ${trust[key]} / 100`}
                >
                  <div
                    className="h-full rounded-full bg-focus-500"
                    style={{ width: `${Math.max(2, trust[key])}%` }}
                  />
                </div>
                <span className="w-8 text-right text-xs tabular-nums text-ink-500 dark:text-ink-400">
                  {trust[key]}
                </span>
              </dd>
            </div>
          ))}
        </dl>
      )}
    </section>
  );
}

function ShieldIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M12 2 4 5v6c0 5 3.4 9.4 8 11 4.6-1.6 8-6 8-11V5z" opacity={0.85} />
    </svg>
  );
}

function ChevronIcon({ open }: { open: boolean }) {
  return (
    <svg
      className={`h-5 w-5 shrink-0 text-ink-400 transition-transform ${open ? 'rotate-180' : ''}`}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      aria-hidden="true"
    >
      <path strokeLinecap="round" strokeLinejoin="round" d="m6 9 6 6 6-6" />
    </svg>
  );
}
