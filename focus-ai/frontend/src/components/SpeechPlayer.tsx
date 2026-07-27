'use client';

import { useCallback, useId, useRef, useState } from 'react';
import { api, describeError } from '@/lib/api';
import { speech, useSpeech } from '@/lib/speech';

export interface SpeechSource {
  slug: string;
  title: string;
}

/**
 * The button that starts listening. Nothing else.
 *
 * It used to be a card carrying the title, a progress bar, five speed chips and
 * a voice select — a panel of controls sitting in the middle of an article,
 * mostly disabled, for a feature the reader had not asked for yet. Everything
 * that is only meaningful once audio is playing now lives in {@link SpeechDock},
 * which appears at the bottom of the screen when it becomes true.
 *
 * The split also fixes something the card could not: the dock is mounted by the
 * layout, so it survives navigation. The queue keeps playing and stays
 * controllable while the reader moves through the app, instead of losing its
 * controls the moment the page that drew them unmounted.
 *
 * The script is fetched on the first press rather than with the page: it is a
 * second copy of the story's text, and most readers never listen. Sources are
 * read one after another and fetched as they come up, so the day queue costs one
 * request per story actually reached rather than twenty on arrival.
 */
export function SpeechPlayer({
  sources,
  label,
  className = '',
  variant = 'pill',
}: {
  sources: SpeechSource[];
  label?: string;
  className?: string;
  /**
   * `round` is the big circle that anchors the top of a story — glyph only, no
   * label, sized to read as the primary action next to the icon row above it.
   * It never renders the setup panel: it sits in a narrow column beside the
   * headline, where a block of instructions would not fit. Pages using it pair
   * it with {@link VoiceSetupNotice}, which has the width to say the same thing.
   */
  variant?: 'pill' | 'round';
}) {
  const id = useId();
  const state = useSpeech();

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Read inside the finished-callback, which is registered once per source and
  // must see the latest list without being re-registered.
  const sourcesRef = useRef(sources);
  sourcesRef.current = sources;

  const mine = state.owner === id;
  const speaking = mine && state.status === 'speaking';
  const paused = mine && state.status === 'paused';

  const start = useCallback(
    async (index: number) => {
      const list = sourcesRef.current;
      if (index >= list.length) return;

      setLoading(true);
      setError(null);

      try {
        const script = await api.speech(list[index].slug);

        speech.play(script.chunks, {
          title: script.title,
          owner: id,
          // Handed over even when the rendering is expected to work: if it 404s
          // the engine falls back to reading these chunks aloud, and by then the
          // gesture that authorised playback is gone, so there is no second
          // chance to fetch them.
          audioUrl: api.stories.audioUrl(list[index].slug),
          queue: list.length > 1 ? { index: index + 1, total: list.length } : undefined,
          onFinished: () => {
            // Chained here rather than by concatenating every script up front:
            // the reader usually stops after two or three.
            if (index + 1 < sourcesRef.current.length) {
              void start(index + 1);
            }
          },
        });
      } catch (caught) {
        setError(describeError(caught, 'Okuma metni alınamadı.'));
      } finally {
        setLoading(false);
      }
    },
    [id],
  );

  const round = variant === 'round';

  if (!state.supported) return null;

  /*
   * No longer gated on the device having a Turkish voice.
   *
   * That check made sense when the device's voice was the only engine: a play
   * button that could only produce English vowels reading Turkish suffixes was
   * worse than no button. The server renders the audio now, and it does so on
   * exactly the devices that have no usable voice of their own — so waiting for
   * a voice list, or hiding the control when it comes back empty, would withhold
   * the feature from the readers it was built for.
   *
   * VoiceSetupHelp still exists and still says something true; it is shown by
   * VoiceSetupNotice further down the page, where it does not stand between the
   * reader and a button that works.
   */

  const toggle = () => {
    if (speaking) {
      speech.pause();
      return;
    }

    if (paused) {
      speech.resume();
      return;
    }

    // Must run inside the gesture: iOS refuses to start speech otherwise, and
    // does so silently.
    void start(0);
  };

  const text = label ?? 'Dinle';
  const glyph = round ? 'h-7 w-7' : 'h-3.5 w-3.5';

  const icon = loading ? (
    <span
      className={`${glyph} animate-spin rounded-full border-2 border-current border-t-transparent`}
      aria-hidden="true"
    />
  ) : speaking ? (
    <svg className={glyph} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M8 5h3v14H8zM13 5h3v14h-3z" />
    </svg>
  ) : (
    // Nudged right so the triangle's optical centre lands on the circle's.
    <svg className={`${glyph} ${round ? 'translate-x-[2px]' : ''}`} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M7 4.5v15l13-7.5z" />
    </svg>
  );

  const title = speaking ? 'Duraklat' : paused ? 'Devam et' : round ? 'Sesli oku' : `${text} — sesli oku`;

  if (round) {
    return (
      <div className={`flex flex-col items-end gap-1 ${className}`}>
        <button
          type="button"
          onClick={toggle}
          disabled={loading}
          aria-label={title}
          title={title}
          className="flex h-16 w-16 items-center justify-center rounded-full bg-focus-600 text-white
                     shadow-lg shadow-focus-600/25 transition hover:bg-focus-700 active:scale-95
                     active:bg-focus-800 disabled:opacity-60 dark:shadow-focus-900/40"
        >
          {icon}
        </button>

        {error && <p className="max-w-[10rem] text-right text-[11px] text-signal-hype">{error}</p>}
      </div>
    );
  }

  return (
    <div className={className}>
      <button
        type="button"
        onClick={toggle}
        disabled={loading}
        aria-label={title}
        className={`tap-44 inline-flex items-center gap-2 rounded-full border px-3.5 py-2 text-[13px]
                    font-medium transition disabled:opacity-60 ${
                      speaking || paused
                        ? 'border-transparent bg-focus-600 text-white hover:bg-focus-700'
                        : `border-ink-200 bg-white text-ink-700 hover:border-focus-300 hover:text-focus-700
                           dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200
                           dark:hover:border-focus-700 dark:hover:text-focus-300`
                    }`}
      >
        {icon}
        {text}
      </button>

      {error && <p className="mt-1.5 text-xs text-signal-hype">{error}</p>}
    </div>
  );
}
