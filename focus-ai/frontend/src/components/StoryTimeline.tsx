import { timeAgo, trUpper } from '@/lib/format';
import type { StoryPhase, Timeline, TimelineMomentKind } from '@/lib/types';

/**
 * How the story developed, and whether it is still developing.
 *
 * This answers the question the rest of the page does not: is what I am reading
 * the final shape, or is it going to look different tomorrow? Someone deciding
 * whether to act on a development needs that more than another list of outlets —
 * and the coverage comparison below deliberately answers the other half, what the
 * outlets said rather than when.
 *
 * Only moments that changed what the story is get a line. A row per article would
 * just be the source list again in a different order.
 */

const PHASE: Record<StoryPhase, { label: string; note: string; chip: string; dot: string }> = {
  Breaking: {
    label: 'gelişiyor',
    note: 'Yeni kırıldı — tablo bugün değişebilir.',
    chip: 'bg-signal-hype/15 text-signal-hype',
    dot: 'bg-signal-hype',
  },
  Developing: {
    label: 'sürüyor',
    note: 'Yeni kaynaklar hâlâ geliyor.',
    chip: 'bg-signal-caution/15 text-signal-caution',
    dot: 'bg-signal-caution',
  },
  Settled: {
    label: 'durdu',
    note: 'Bir süredir yeni bir şey gelmedi; büyük ihtimalle son hâli bu.',
    chip: 'bg-ink-100 text-ink-500 dark:bg-ink-800 dark:text-ink-400',
    dot: 'bg-ink-300 dark:bg-ink-700',
  },
};

const MOMENT_LABEL: Record<TimelineMomentKind, string> = {
  FirstReport: 'ilk veren',
  OfficialConfirmation: 'resmî kaynak doğruladı',
  Resurgence: 'yeniden gündeme geldi',
  LatestReport: 'son gelişme',
};

/** Turkish duration phrase. Hours below two days, then days. */
function span(hours: number): string {
  if (hours < 1) return 'aynı saat içinde';
  if (hours < 48) return `${Math.round(hours)} saate yayıldı`;
  return `${Math.round(hours / 24)} güne yayıldı`;
}

export function StoryTimeline({ timeline }: { timeline: Timeline }) {
  const phase = PHASE[timeline.phase];

  // One outlet reporting once has no shape to show — the source list below says
  // everything a timeline could.
  if (timeline.moments.length < 2 && timeline.outletCount < 2) return null;

  return (
    <section className="card p-4 sm:p-5">
      <header className="mb-3 flex flex-wrap items-baseline gap-x-2 gap-y-1">
        <h2 className="text-sm font-semibold text-ink-800 dark:text-ink-100">Nasıl gelişti?</h2>
        <span className={`chip text-[10.5px] ${phase.chip}`}>{phase.label}</span>
        <span className="text-xs text-ink-500 dark:text-ink-400">
          {timeline.outletCount} kaynak · {span(timeline.spanHours)}
        </span>
      </header>

      <ol className="relative space-y-3 border-l border-ink-200 pl-4 dark:border-ink-800">
        {timeline.moments.map((moment) => (
          <li key={`${moment.kind}-${moment.at}`} className="relative">
            <span
              className={`absolute -left-[21px] top-1.5 h-2 w-2 rounded-full ring-2 ring-white dark:ring-ink-900 ${
                moment.kind === 'OfficialConfirmation' ? 'bg-signal-trust' : 'bg-ink-300 dark:bg-ink-600'
              }`}
              aria-hidden="true"
            />
            <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
              <span className="font-sans text-[11px] tracking-wide text-ink-500 dark:text-ink-400">
                {trUpper(MOMENT_LABEL[moment.kind])}
              </span>
              <span className="text-sm text-ink-800 dark:text-ink-100">{moment.sourceName}</span>
              <span className="text-[11px] text-ink-400 dark:text-ink-500">{timeAgo(moment.at)}</span>
            </div>
          </li>
        ))}
      </ol>

      <p className="mt-3 text-[11.5px] text-ink-500 dark:text-ink-400">{phase.note}</p>
    </section>
  );
}
