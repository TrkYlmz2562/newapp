import type { Commitment } from '@/lib/types';

/**
 * How firm a finance development is, in one phrase.
 *
 * This replaces the Finans section. The classification was always the valuable
 * part — what was decided, by whom, and the sentence in the source that says so —
 * and none of it needed an address of its own. On the card it is a chip; on the
 * detail page the same data opens out with the quote attached.
 *
 * There is deliberately no number. A percentage beside a money claim reads as a
 * precision this product does not have, so the reader gets the tier's name and
 * the receipt instead.
 */

const TIER_LABEL: Record<Commitment['tier'], string> = {
  Realized: 'gerçekleşti',
  EnactedDated: 'yürürlükte',
  OfficialCommitment: 'resmî karar',
  ConditionalPending: 'onay bekliyor',
  StatedIntent: 'niyet',
  UnverifiedClaim: 'doğrulanmadı',
  AnalystSpeculation: 'analist yorumu',
  Unknown: 'sınıflandırılmadı',
};

function tone(commitment: Commitment): string {
  if (commitment.isConditional) return 'bg-signal-caution/15 text-signal-caution';
  if (commitment.isSettled) return 'bg-signal-trust/15 text-signal-trust';
  return 'bg-ink-100 text-ink-600 dark:bg-ink-800 dark:text-ink-300';
}

/** Compact form for a story card. */
export function CommitmentBadge({ commitment }: { commitment: Commitment }) {
  return (
    <span className={`chip text-[10.5px] ${tone(commitment)}`}>
      {TIER_LABEL[commitment.tier]}
      {commitment.dateText && ` · ${commitment.dateText}`}
    </span>
  );
}

/** Full form for the detail page, with the sentence the classification rests on. */
export function CommitmentPanel({ commitment }: { commitment: Commitment }) {
  return (
    <section className="card p-4 sm:p-5">
      <header className="mb-2 flex flex-wrap items-baseline gap-2">
        <h2 className="text-sm font-semibold text-ink-800 dark:text-ink-100">Ne karara bağlandı?</h2>
        <CommitmentBadge commitment={commitment} />
      </header>

      {commitment.event && (
        <p className="font-serif leading-relaxed text-ink-800 dark:text-ink-100">{commitment.event}</p>
      )}

      <dl className="mt-3 space-y-1.5 font-mono text-[11.5px] text-ink-500 dark:text-ink-400">
        {commitment.dateText && (
          <div className="flex gap-2">
            <dt className="w-20 shrink-0">tarih</dt>
            <dd className="text-ink-700 dark:text-ink-200">{commitment.dateText}</dd>
          </div>
        )}
        {commitment.reference && (
          <div className="flex gap-2">
            <dt className="w-20 shrink-0">dayanak</dt>
            <dd className="text-ink-700 dark:text-ink-200">{commitment.reference}</dd>
          </div>
        )}
        {commitment.condition && (
          <div className="flex gap-2">
            <dt className="w-20 shrink-0">şart</dt>
            <dd className="text-ink-700 dark:text-ink-200">{commitment.condition}</dd>
          </div>
        )}
      </dl>

      {/* The receipt. The backend enforces that this appears literally in the
          source, which is what makes the classification checkable rather than
          something the reader has to take on trust. */}
      {commitment.quote && (
        <blockquote className="mt-3 border-l-2 border-ink-300 pl-3 font-serif text-[13px] italic leading-relaxed text-ink-600 dark:border-ink-700 dark:text-ink-300">
          “{commitment.quote}”
        </blockquote>
      )}

      <p className="mt-3 text-[11px] text-ink-400 dark:text-ink-500">
        Yatırım tavsiyesi değildir. Burada yalnızca kaynağın ne söylediği yazar.
      </p>
    </section>
  );
}
