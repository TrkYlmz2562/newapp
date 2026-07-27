'use client';

import { useEffect, useRef, useState } from 'react';
import { copyText } from '@/lib/clipboard';

type State = 'idle' | 'copied' | 'failed';

/**
 * Copies text and says whether it worked.
 *
 * The failure branch matters more than usual here: on plain HTTP the clipboard
 * API is missing entirely, and if both routes fail the reader needs to be told
 * so they can select the text by hand rather than pasting yesterday's clipboard
 * into their lesson.
 */
export function CopyButton({
  text,
  label,
  hint,
  variant = 'primary',
  onFailed,
}: {
  text: string;
  label: string;
  hint?: string;
  variant?: 'primary' | 'ghost';
  onFailed?: () => void;
}) {
  const [state, setState] = useState<State>('idle');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);

  const copy = async () => {
    const ok = await copyText(text);

    setState(ok ? 'copied' : 'failed');
    if (!ok) onFailed?.();

    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setState('idle'), 2500);
  };

  return (
    <div className="flex-1">
      <button
        type="button"
        onClick={copy}
        className={`${variant === 'primary' ? 'btn-primary' : 'btn-ghost'} w-full`}
      >
        {state === 'copied' ? '✓ Kopyalandı' : state === 'failed' ? 'Kopyalanamadı' : label}
      </button>

      {hint && state === 'idle' && (
        <p className="mt-1 text-center text-[11px] text-ink-400 dark:text-ink-500">{hint}</p>
      )}

      {state === 'failed' && (
        <p role="alert" className="mt-1 text-center text-[11px] text-signal-hype">
          Tarayıcı izin vermedi — metni aşağıdan elle seçebilirsin.
        </p>
      )}
    </div>
  );
}
