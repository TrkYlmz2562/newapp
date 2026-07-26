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
      setTimeout(() => setState('idle'), 2500);
    } catch (error) {
      // Dismissing the share sheet rejects with AbortError; that is not a failure.
      if (error instanceof DOMException && error.name === 'AbortError') {
        setState('idle');
        return;
      }

      setState('error');
      setTimeout(() => setState('idle'), 3000);
    }
  };

  const label =
    state === 'working'
      ? 'Hazırlanıyor…'
      : state === 'saved'
        ? '✓ İndirildi'
        : state === 'error'
          ? 'Olmadı, tekrar dene'
          : 'Görsel paylaş';

  return (
    <button
      type="button"
      onClick={share}
      disabled={state === 'working'}
      className="btn-ghost px-3 py-1.5 text-xs"
      aria-live="polite"
    >
      <svg
        className="h-4 w-4"
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
      {label}
    </button>
  );
}
