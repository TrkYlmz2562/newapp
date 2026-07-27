'use client';

import { useEffect, useRef, useState } from 'react';
import { trUpper } from '@/lib/format';
import { detectPlatform, hasRestrictedVoiceList, type Platform } from '@/lib/platform';
import { SPEECH_RATES, speech, useSpeech } from '@/lib/speech';

/**
 * The now-playing island.
 *
 * Mounted once by the layout, above the tab bar, and shown only while something
 * is being read. Two things follow from living there rather than in the article:
 * the controls do not take up a reader's page before they are worth anything,
 * and they survive navigation — the day queue keeps playing, and stays pausable,
 * while the reader moves around the app.
 *
 * Collapsed it is the four things wanted mid-listen: what is playing, how far in,
 * pause, stop. Speed, sentence skip and voice are a tap away rather than on
 * screen, because they are set once and then never touched again.
 */
/** Seconds as m:ss. Unknown durations read as 0:00 rather than NaN. */
function clock(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00';

  const total = Math.floor(seconds);
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

export function SpeechDock() {
  const state = useSpeech();
  const [expanded, setExpanded] = useState(false);
  const [platform, setPlatform] = useState<Platform>('unknown');
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => setPlatform(detectPlatform()), []);

  const visible = state.supported && state.status !== 'idle';

  /*
   * Keeps the page's bottom padding in step with the island's height.
   *
   * The island is fixed, so it cannot push anything: without this the last card
   * of the feed sits underneath it, and expanding the controls hides more of it.
   * The layout reserves `--dock-h` on top of its own padding; measuring rather
   * than hard-coding is what keeps the two in agreement when the panel opens.
   */
  useEffect(() => {
    const element = ref.current;
    const root = document.documentElement;

    if (!element) {
      root.style.removeProperty('--dock-h');
      return;
    }

    const measure = () => root.style.setProperty('--dock-h', `${element.offsetHeight + 12}px`);

    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);

    return () => {
      observer.disconnect();
      root.style.removeProperty('--dock-h');
    };
  }, [visible]);

  // Collapse on the way out, so the next thing played opens compact rather than
  // restoring a panel the reader opened for a different story.
  useEffect(() => {
    if (!visible) setExpanded(false);
  }, [visible]);

  if (!visible) return null;

  const speaking = state.status === 'speaking';
  const ended = state.status === 'ended';
  const onAudio = state.engine === 'audio';

  // One number for two engines: the controller reconciles sentences and seconds
  // so nothing here has to know which is playing.
  const progress = state.progress;

  const toggle = () => {
    if (speaking) {
      speech.pause();
      return;
    }

    speech.resume();
  };

  return (
    <div
      className="pointer-events-none fixed inset-x-0 z-30 px-3"
      style={{ bottom: 'calc(env(safe-area-inset-bottom) + 4.25rem)' }}
    >
      <div
        ref={ref}
        role="region"
        aria-label="Çalan"
        className="dock-in pointer-events-auto mx-auto w-full max-w-3xl overflow-hidden rounded-2xl
                   border border-ink-200/70 bg-white/85 shadow-lg shadow-ink-900/10 backdrop-blur-xl
                   dark:border-ink-800 dark:bg-ink-900/85 dark:shadow-black/40"
      >
        {/* Hairline progress across the top: legible at a glance, and it costs
            no height in a bar that is trying to stay out of the way. */}
        <div className="h-0.5 w-full bg-ink-200/70 dark:bg-ink-800">
          <div
            className="h-full bg-focus-600 transition-[width] duration-300 dark:bg-focus-400"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(progress * 100)}
            style={{ width: `${progress * 100}%` }}
          />
        </div>

        <div className="flex items-center gap-3 p-2.5">
          <button
            type="button"
            onClick={toggle}
            aria-label={speaking ? 'Duraklat' : ended ? 'Baştan oku' : 'Devam et'}
            className="tap-44 flex h-10 w-10 flex-none items-center justify-center rounded-full
                       bg-focus-600 text-white transition hover:bg-focus-700 active:bg-focus-800"
          >
            {speaking ? (
              <svg className="h-4 w-4" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M8 5h3v14H8zM13 5h3v14h-3z" />
              </svg>
            ) : ended ? (
              <svg
                className="h-4 w-4"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={2}
                aria-hidden="true"
              >
                <path strokeLinecap="round" d="M20 12a8 8 0 1 1-2.6-5.9M20 4v4h-4" />
              </svg>
            ) : (
              <svg className="h-4 w-4 translate-x-[1px]" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M7 4.5v15l13-7.5z" />
              </svg>
            )}
          </button>

          {/* The title area opens the panel. A wide, obvious target for a
              secondary action, without spending a button on it. */}
          <button
            type="button"
            onClick={() => setExpanded((open) => !open)}
            aria-expanded={expanded}
            aria-label="Okuma ayarları"
            className="min-w-0 flex-1 text-left"
          >
            <p className="truncate text-[13px] font-medium text-ink-800 dark:text-ink-100">
              {state.title ?? 'Sesli okuma'}
            </p>
            <p className="mt-0.5 font-mono text-[11px] text-ink-400 dark:text-ink-500">
              {ended
                ? 'bitti'
                : onAudio
                  ? `${clock(state.position)} / ${clock(state.duration)}`
                  : `${state.index + 1}/${state.total} cümle`}
              {state.queueTotal > 1 && ` · ${state.queueIndex}/${state.queueTotal} haber`}
              {state.rate !== 1 && ` · ${state.rate.toLocaleString('tr-TR')}×`}
            </p>
          </button>

          <button
            type="button"
            onClick={() => setExpanded((open) => !open)}
            aria-expanded={expanded}
            aria-label={expanded ? 'Ayarları kapat' : 'Ayarları aç'}
            className="tap-row flex-none rounded-lg p-2 text-ink-400 transition hover:bg-ink-100
                       hover:text-ink-700 dark:hover:bg-ink-800 dark:hover:text-ink-200"
          >
            <svg
              className={`h-4 w-4 transition-transform ${expanded ? 'rotate-180' : ''}`}
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={2}
              aria-hidden="true"
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="m6 15 6-6 6 6" />
            </svg>
          </button>

          <button
            type="button"
            onClick={() => speech.stop()}
            aria-label="Kapat"
            className="tap-row flex-none rounded-lg p-2 text-ink-400 transition hover:bg-ink-100
                       hover:text-ink-700 dark:hover:bg-ink-800 dark:hover:text-ink-200"
          >
            <svg
              className="h-4 w-4"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={2}
              aria-hidden="true"
            >
              <path strokeLinecap="round" d="m6 6 12 12M18 6 6 18" />
            </svg>
          </button>
        </div>

        {expanded && (
          <div className="border-t border-ink-200/70 px-2.5 pb-2.5 pt-2 dark:border-ink-800">
            <div className="flex items-center gap-2">
              {/* A file has no sentences to step through, so the same two buttons
                  move fifteen seconds instead — which is also what the lock
                  screen offers, so the two agree. */}
              <button
                type="button"
                onClick={() => speech.skip(-1)}
                disabled={!onAudio && state.index <= 0}
                className="tap-row rounded-lg px-2 py-1 font-mono text-[11px] text-ink-500 transition
                           hover:bg-ink-100 disabled:opacity-30 dark:text-ink-400 dark:hover:bg-ink-800"
              >
                {onAudio ? '‹ 15 sn' : '‹ cümle'}
              </button>
              <button
                type="button"
                onClick={() => speech.skip(1)}
                disabled={!onAudio && state.index >= state.total - 1}
                className="tap-row rounded-lg px-2 py-1 font-mono text-[11px] text-ink-500 transition
                           hover:bg-ink-100 disabled:opacity-30 dark:text-ink-400 dark:hover:bg-ink-800"
              >
                {onAudio ? '15 sn ›' : 'cümle ›'}
              </button>
            </div>

            <div className="mt-2 flex gap-1.5" role="group" aria-label="Okuma hızı">
              {SPEECH_RATES.map((rate) => (
                <button
                  key={rate}
                  type="button"
                  onClick={() => speech.setRate(rate)}
                  aria-pressed={state.rate === rate}
                  className={`tap-row flex-1 rounded-lg px-2 py-1 font-mono text-[11px] transition ${
                    state.rate === rate
                      ? 'bg-focus-600 text-white'
                      : 'bg-ink-100 text-ink-600 hover:bg-ink-200 dark:bg-ink-800 dark:text-ink-300 dark:hover:bg-ink-700'
                  }`}
                >
                  {rate.toLocaleString('tr-TR')}×
                </button>
              ))}
            </div>

            {/* The voice is shown even when there is only one of it. Hiding the
                row was how a reader who installed a new voice on the device
                ended up with no way to tell whether the app had ignored it or
                never saw it. */}
            <div className="mt-2 flex items-center gap-2">
              <span className="flex-none font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
                {trUpper('ses')}
              </span>

              {/* The device's voice list has nothing to say about a file the
                  server rendered. Showing a picker that cannot change what is
                  playing would be worse than showing nothing. */}
              {onAudio ? (
                <span className="min-w-0 flex-1 truncate text-[13px] text-ink-700 dark:text-ink-200">
                  Focus AI
                </span>
              ) : state.turkishVoices.length > 1 ? (
                <select
                  value={state.voiceUri ?? ''}
                  onChange={(event) => speech.setVoice(event.target.value)}
                  aria-label="Okuma sesi"
                  className="min-w-0 flex-1 rounded-lg border border-ink-200 bg-white px-2 py-1 text-[13px]
                             text-ink-700 dark:border-ink-700 dark:bg-ink-950 dark:text-ink-200"
                >
                  {state.turkishVoices.map((voice) => (
                    <option key={voice.voiceURI} value={voice.voiceURI}>
                      {voice.name}
                    </option>
                  ))}
                </select>
              ) : (
                <span className="min-w-0 flex-1 truncate text-[13px] text-ink-700 dark:text-ink-200">
                  {state.turkishVoices[0]?.name}
                </span>
              )}
            </div>

            {!onAudio && state.turkishVoices.length === 1 && hasRestrictedVoiceList(platform) && (
              <p className="mt-1.5 text-[12px] leading-relaxed text-ink-500 dark:text-ink-400">
                Bu cihazın tarayıcısına tek Türkçe ses açılıyor. Ayarlar'daki Siri sesleri
                ve indirilebilir kaliteli sesler web'e kapalı.
              </p>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
