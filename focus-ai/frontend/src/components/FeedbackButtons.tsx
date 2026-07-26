'use client';

import { useState } from 'react';
import { api } from '@/lib/api';
import type { StoryFeedback } from '@/lib/types';
import { useAuth } from './AuthProvider';

interface Props {
  storyId: string;
  initial?: StoryFeedback | null;
  surface?: string;
  className?: string;
}

/**
 * "Faydalı" / "Az göster" — the reader's own verdict on a story.
 *
 * This is the cheapest personalisation signal there is: no model call, no
 * embedding, just two taps that the ranker reads back as topic-level feedback.
 * A verdict never removes anything. "Az göster" demotes hard, but a story big
 * enough still surfaces, because the reader asked for less of it — not for it
 * to be hidden from them.
 *
 * Tapping the active verdict again clears it. That matters more than it looks:
 * the buttons sit next to a scrolling thumb, and an accidental "az göster"
 * that could not be taken back would quietly reshape the feed.
 *
 * Icon only. The label lives in aria-label and title — dropping the visible
 * text is a design choice, dropping the accessible name would leave a screen
 * reader with two unnamed buttons.
 */
export function FeedbackButtons({ storyId, initial = null, surface, className = '' }: Props) {
  const { user } = useAuth();
  const [choice, setChoice] = useState<StoryFeedback | null>(initial);
  const [busy, setBusy] = useState(false);

  // Nothing to record against an anonymous reader, so nothing to offer them.
  if (!user) return null;

  const vote = async (verdict: StoryFeedback, event: React.MouseEvent) => {
    // These often sit inside the card's own <Link>; a vote must not navigate.
    event.preventDefault();
    event.stopPropagation();

    if (busy) return;

    const next = choice === verdict ? null : verdict;
    const previous = choice;

    // Optimistic: the tap is cheap to reverse and the round trip is visible.
    setChoice(next);
    setBusy(true);

    try {
      if (next === null) {
        await api.stories.clearFeedback(storyId);
      } else {
        await api.stories.recordInteraction(storyId, next, undefined, surface);
      }
    } catch {
      setChoice(previous);
    } finally {
      setBusy(false);
    }
  };

  // tap-44 grows the touch area to 44px without growing the icon or the row.
  const base =
    'tap-row p-1.5 transition hover:bg-ink-100 disabled:opacity-50 dark:hover:bg-ink-800';

  return (
    <div className={`flex items-center gap-1 ${className}`}>
      <button
        type="button"
        onClick={(event) => vote('Helpful', event)}
        disabled={busy}
        aria-pressed={choice === 'Helpful'}
        aria-label={choice === 'Helpful' ? 'Faydalı işaretini kaldır' : 'Faydalı buldum'}
        title={choice === 'Helpful' ? 'Faydalı işaretini kaldır' : 'Faydalı buldum'}
        className={`${base} ${
          choice === 'Helpful' ? 'text-signal-trust' : 'text-ink-400 dark:text-ink-500'
        }`}
      >
        <svg
          className="h-4 w-4"
          viewBox="0 0 24 24"
          fill={choice === 'Helpful' ? 'currentColor' : 'none'}
          stroke="currentColor"
          strokeWidth={1.8}
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M7 10v11H4a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1z" />
          <path d="M7 10l4.5-7a2.2 2.2 0 0 1 3.2 2.7L13.5 9h5.2a2 2 0 0 1 2 2.4l-1.4 7A2 2 0 0 1 17.3 20H7z" />
        </svg>
      </button>

      <button
        type="button"
        onClick={(event) => vote('NotHelpful', event)}
        disabled={busy}
        aria-pressed={choice === 'NotHelpful'}
        aria-label={choice === 'NotHelpful' ? 'Bu tercihi geri al' : 'Bu tür haberleri az göster'}
        title={choice === 'NotHelpful' ? 'Bu tercihi geri al' : 'Bu tür haberleri az göster'}
        className={`${base} ${
          choice === 'NotHelpful' ? 'text-signal-hype' : 'text-ink-400 dark:text-ink-500'
        }`}
      >
        <svg
          className="h-4 w-4"
          viewBox="0 0 24 24"
          fill={choice === 'NotHelpful' ? 'currentColor' : 'none'}
          stroke="currentColor"
          strokeWidth={1.8}
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M17 14V3h3a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1z" />
          <path d="M17 14l-4.5 7a2.2 2.2 0 0 1-3.2-2.7L10.5 15H5.3a2 2 0 0 1-2-2.4l1.4-7A2 2 0 0 1 6.7 4H17z" />
        </svg>
      </button>
    </div>
  );
}
