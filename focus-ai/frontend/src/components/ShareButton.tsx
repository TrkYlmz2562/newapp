'use client';

import { useState } from 'react';
import { renderShareImage } from '@/lib/shareImage';
import type { StoryDetail } from '@/lib/types';

type State = 'idle' | 'working' | 'saved' | 'error';

/**
 * Turns a story into a picture and hands it to the phone.
 *
 * The native share sheet is the whole point: it means this component never has to
 * know about WhatsApp, or any other destination — the reader picks. Where the
 * Web Share API cannot take files (desktop browsers, older iOS), the image is
 * downloaded instead, which still gets it into a chat with one more step.
 *
 * Icon only, so the state has to be carried by the icon itself. The label stays
 * in aria-label and title: dropping the visible text is a design choice, dropping
 * the accessible name would make the control invisible to a screen reader.
 */
export function ShareButton({ story }: { story: StoryDetail }) {
  const [state, setState] = useState<State>('idle');

  const share = async () => {
    setState('working');

    try {
      const blob = await renderShareImage(story, window.location.origin);
      const file = new File([blob], `focus-ai-${story.slug}.png`, { type: 'image/png' });

      // canShare must be asked about the actual file: iOS reports share support
      // broadly but refuses some payloads, and calling share() blind then throws.
      if (navigator.canShare?.({ files: [file] })) {
        await navigator.share({ files: [file], title: story.title });
        setState('idle');
        return;
      }

      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = file.name;
      anchor.click();
      URL.revokeObjectURL(url);
      setState('saved');
      setTimeout(() => setState('idle'), 2000);
    } catch (error) {
      // Dismissing the share sheet rejects with AbortError; that is not a failure.
      if (error instanceof DOMException && error.name === 'AbortError') {
        setState('idle');
        return;
      }

      setState('error');
      setTimeout(() => setState('idle'), 2500);
    }
  };

  const label =
    state === 'working'
      ? 'Görsel hazırlanıyor'
      : state === 'saved'
        ? 'Görsel indirildi'
        : state === 'error'
          ? 'Görsel oluşturulamadı, tekrar dene'
          : 'Görsel olarak paylaş';

  return (
    <button
      type="button"
      onClick={share}
      disabled={state === 'working'}
      aria-label={label}
      title={label}
      className={`tap-44 p-2 transition hover:bg-ink-100 disabled:opacity-60 dark:hover:bg-ink-800 ${
        state === 'error' ? 'text-signal-hype' : 'text-ink-500 dark:text-ink-400'
      }`}
    >
      {/* Announced to assistive tech, which never sees the icon swap. */}
      <span className="sr-only" aria-live="polite">
        {label}
      </span>

      {state === 'working' ? (
        <svg className="h-5 w-5 animate-spin" viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth={2} opacity={0.25} />
          <path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" strokeWidth={2} strokeLinecap="round" />
        </svg>
      ) : state === 'saved' ? (
        <svg
          className="h-5 w-5 text-signal-trust"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="m5 13 4 4L19 7" />
        </svg>
      ) : (
        <svg
          className="h-5 w-5"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.8}
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M12 15V3m0 0L8 7m4-4 4 4" />
          <path d="M4 13v6a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-6" />
        </svg>
      )}
    </button>
  );
}
