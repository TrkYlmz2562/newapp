'use client';

import { useCallback, useId, useRef, useState } from 'react';
import { api, describeError } from '@/lib/api';
import { speech, useSpeech } from '@/lib/speech';
import { VoiceSetupHelp } from './VoiceSetupHelp';

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
  quiet = false,
}: {
  sources: SpeechSource[];
  label?: string;
  className?: string;
  /**
   * Render nothing when the device has no Turkish voice, instead of the setup
   * panel. For the feed: a reader who never wanted audio should not have the top
   * of their day taken by instructions for a feature they did not ask about.
   */
  quiet?: boolean;
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

  if (!state.supported) return null;

  // Still resolving the voice list — Safari hands it over asynchronously, and
  // offering a play button that would fail is worse than a moment of nothing.
  if (!state.ready) {
    return <div className={`skeleton h-9 w-32 rounded-full ${className}`} />;
  }

  if (state.turkishVoices.length === 0) {
    return quiet ? null : <VoiceSetupHelp className={className} />;
  }

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

  return (
    <div className={className}>
      <button
        type="button"
        onClick={toggle}
        disabled={loading}
        aria-label={speaking ? 'Duraklat' : paused ? 'Devam et' : `${text} — sesli oku`}
        className={`tap-44 inline-flex items-center gap-2 rounded-full border px-3.5 py-2 text-[13px]
                    font-medium transition disabled:opacity-60 ${
                      speaking || paused
                        ? 'border-transparent bg-focus-600 text-white hover:bg-focus-700'
                        : `border-ink-200 bg-white text-ink-700 hover:border-focus-300 hover:text-focus-700
                           dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200
                           dark:hover:border-focus-700 dark:hover:text-focus-300`
                    }`}
      >
        {loading ? (
          <span
            className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
            aria-hidden="true"
          />
        ) : speaking ? (
          <svg className="h-3.5 w-3.5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M8 5h3v14H8zM13 5h3v14h-3z" />
          </svg>
        ) : (
          <svg className="h-3.5 w-3.5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M7 4.5v15l13-7.5z" />
          </svg>
        )}
        {text}
      </button>

      {error && <p className="mt-1.5 text-xs text-signal-hype">{error}</p>}
    </div>
  );
}
