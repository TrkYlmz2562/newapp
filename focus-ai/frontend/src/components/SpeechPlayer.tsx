'use client';

import { useCallback, useId, useRef, useState } from 'react';
import { api, describeError } from '@/lib/api';
import { trUpper } from '@/lib/format';
import { SPEECH_RATES, speech, useSpeech } from '@/lib/speech';
import { VoiceSetupHelp } from './VoiceSetupHelp';

export interface SpeechSource {
  slug: string;
  title: string;
}

/**
 * Play, speed, and where you are in the article.
 *
 * The script is fetched on the first press rather than with the page: it is a
 * second copy of the story's text, and most readers never listen.
 *
 * Sources are read one after another and fetched as they come up, so the day
 * queue costs one request per story actually reached rather than twenty on
 * arrival.
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
  const [position, setPosition] = useState(0);

  // Read inside the finished-callback, which is registered once per source and
  // must see the latest list without being re-registered.
  const sourcesRef = useRef(sources);
  sourcesRef.current = sources;

  const mine = state.owner === id;
  const speaking = mine && state.status === 'speaking';
  const paused = mine && state.status === 'paused';
  const active = speaking || paused;

  const start = useCallback(
    async (index: number) => {
      const list = sourcesRef.current;
      if (index >= list.length) return;

      setLoading(true);
      setError(null);
      setPosition(index);

      try {
        const script = await api.speech(list[index].slug);

        speech.play(script.chunks, {
          title: script.title,
          owner: id,
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
    return (
      <div className={`card p-4 ${className}`}>
        <div className="skeleton h-9 w-full" />
      </div>
    );
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

  const progress = active && state.total > 0 ? (state.index + 1) / state.total : 0;

  return (
    <section className={`card p-4 ${className}`} aria-label="Sesli okuma">
      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={toggle}
          disabled={loading}
          aria-label={speaking ? 'Duraklat' : paused ? 'Devam et' : 'Sesli oku'}
          className="tap-44 flex h-11 w-11 flex-none items-center justify-center rounded-full
                     bg-focus-600 text-white transition hover:bg-focus-700 active:bg-focus-800
                     disabled:opacity-60"
        >
          {loading ? (
            <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
          ) : speaking ? (
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M8 5h3v14H8zM13 5h3v14h-3z" />
            </svg>
          ) : (
            <svg className="h-5 w-5 translate-x-[1px]" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M7 4.5v15l13-7.5z" />
            </svg>
          )}
        </button>

        <div className="min-w-0 flex-1">
          <p className="font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
            {trUpper(label ?? 'Sesli oku')}
          </p>

          {active ? (
            <>
              <p className="truncate text-[13px] text-ink-700 dark:text-ink-200">{state.title}</p>
              <div className="mt-1.5 flex items-center gap-2">
                <div
                  className="h-1 flex-1 overflow-hidden rounded-full bg-ink-200 dark:bg-ink-800"
                  role="progressbar"
                  aria-valuemin={0}
                  aria-valuemax={state.total}
                  aria-valuenow={state.index + 1}
                >
                  <div
                    className="h-full bg-focus-600 transition-[width] duration-300 dark:bg-focus-400"
                    style={{ width: `${progress * 100}%` }}
                  />
                </div>
                <span className="flex-none font-mono text-[11px] text-ink-400 dark:text-ink-500">
                  {state.index + 1}/{state.total}
                </span>
              </div>
            </>
          ) : (
            <p className="truncate text-[13px] text-ink-500 dark:text-ink-400">
              {sources.length > 1 ? `${sources.length} haber` : sources[0]?.title}
            </p>
          )}
        </div>

        {active && (
          <button
            type="button"
            onClick={() => speech.stop()}
            aria-label="Durdur"
            className="tap-44 flex-none rounded-lg p-2 text-ink-400 transition hover:bg-ink-100
                       hover:text-ink-700 dark:hover:bg-ink-800 dark:hover:text-ink-200"
          >
            <svg className="h-4 w-4" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <rect x="6" y="6" width="12" height="12" rx="1.5" />
            </svg>
          </button>
        )}
      </div>

      {error && <p className="mt-2 text-xs text-signal-hype">{error}</p>}

      {/*
        Speed is shown whether or not anything is playing. Hiding it until
        playback starts means the reader who wants 1,5× has to listen at 1×
        first, and it is the setting they came for.
      */}
      <div className="chip-row mt-3 flex gap-1.5" role="group" aria-label="Okuma hızı">
        {SPEECH_RATES.map((rate) => (
          <button
            key={rate}
            type="button"
            onClick={() => speech.setRate(rate)}
            aria-pressed={state.rate === rate}
            className={`tap-row flex-none rounded-lg px-2.5 py-1 font-mono text-[11px] transition ${
              state.rate === rate
                ? 'bg-focus-600 text-white'
                : 'bg-ink-100 text-ink-600 hover:bg-ink-200 dark:bg-ink-800 dark:text-ink-300 dark:hover:bg-ink-700'
            }`}
          >
            {rate.toLocaleString('tr-TR')}×
          </button>
        ))}
      </div>

      {active && (
        <>
          <div className="mt-3 flex items-center gap-2">
            <button
              type="button"
              onClick={() => speech.skip(-1)}
              disabled={state.index <= 0}
              aria-label="Önceki cümle"
              className="tap-row rounded-lg px-2 py-1 font-mono text-[11px] text-ink-500 transition
                         hover:bg-ink-100 disabled:opacity-30 dark:text-ink-400 dark:hover:bg-ink-800"
            >
              ‹ cümle
            </button>
            <button
              type="button"
              onClick={() => speech.skip(1)}
              disabled={state.index >= state.total - 1}
              aria-label="Sonraki cümle"
              className="tap-row rounded-lg px-2 py-1 font-mono text-[11px] text-ink-500 transition
                         hover:bg-ink-100 disabled:opacity-30 dark:text-ink-400 dark:hover:bg-ink-800"
            >
              cümle ›
            </button>

            {sources.length > 1 && (
              <span className="ml-auto font-mono text-[11px] text-ink-400 dark:text-ink-500">
                {position + 1}. haber
              </span>
            )}
          </div>

          {/* Only worth showing when there is a choice; most devices ship one
              Turkish voice and a select with a single option is furniture. */}
          {state.turkishVoices.length > 1 && (
            <label className="mt-2 flex items-center gap-2">
              <span className="font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
                {trUpper('ses')}
              </span>
              <select
                value={state.voiceUri ?? ''}
                onChange={(event) => speech.setVoice(event.target.value)}
                className="min-w-0 flex-1 rounded-lg border border-ink-200 bg-white px-2 py-1 text-[13px]
                           text-ink-700 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-200"
              >
                {state.turkishVoices.map((voice) => (
                  <option key={voice.voiceURI} value={voice.voiceURI}>
                    {voice.name}
                  </option>
                ))}
              </select>
            </label>
          )}
        </>
      )}
    </section>
  );
}
