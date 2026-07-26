'use client';

import { useEffect, useState } from 'react';

export type Theme = 'light' | 'dark' | 'system';

export const THEME_STORAGE_KEY = 'focusai-theme';

/**
 * Runs before first paint, inlined in <head>. It has to be a blocking script:
 * applying the class from React would let one light frame through on every load,
 * which on a phone at night is the whole reason someone wants dark mode.
 *
 * Kept in sync with THEME_STORAGE_KEY by hand — a bundled import cannot be used
 * here, since the point is that this executes before any bundle does.
 */
export const themeBootstrapScript = `
(function () {
  try {
    var stored = localStorage.getItem('focusai-theme');
    var dark = stored === 'dark' ||
      ((!stored || stored === 'system') &&
        window.matchMedia('(prefers-color-scheme: dark)').matches);
    document.documentElement.classList.toggle('dark', dark);
  } catch (e) {
    /* Private mode blocks localStorage; the OS preference still applies. */
    try {
      document.documentElement.classList.toggle(
        'dark',
        window.matchMedia('(prefers-color-scheme: dark)').matches
      );
    } catch (e2) {}
  }
})();
`;

function apply(theme: Theme) {
  const dark =
    theme === 'dark' ||
    (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);

  document.documentElement.classList.toggle('dark', dark);
}

const OPTIONS: { value: Theme; label: string; icon: string }[] = [
  { value: 'light', label: 'Açık', icon: '☀️' },
  { value: 'dark', label: 'Koyu', icon: '🌙' },
  { value: 'system', label: 'Sistem', icon: '⚙️' },
];

/** Three-way theme control. "Sistem" is the default and follows the OS. */
export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>('system');
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const stored = localStorage.getItem(THEME_STORAGE_KEY) as Theme | null;
    setTheme(stored ?? 'system');
    setReady(true);
  }, []);

  // Following the OS means following it as it changes, not only at load.
  useEffect(() => {
    if (theme !== 'system') return undefined;

    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => apply('system');
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [theme]);

  const choose = (next: Theme) => {
    setTheme(next);
    localStorage.setItem(THEME_STORAGE_KEY, next);
    apply(next);
  };

  return (
    <div
      role="radiogroup"
      aria-label="Tema"
      className="inline-flex border border-ink-200 bg-transparent p-0.5 dark:border-ink-700 dark:bg-ink-900"
    >
      {OPTIONS.map((option) => {
        // Before the stored value is read every option would render unselected,
        // so nothing is marked until it is actually known.
        const active = ready && theme === option.value;

        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => choose(option.value)}
            className={`tap-44 px-3 py-2.5 text-xs font-semibold transition ${
              active
                ? 'bg-ink-900 text-white dark:bg-ink-100 dark:text-ink-900'
                : 'text-ink-600 hover:bg-ink-100 dark:text-ink-300 dark:hover:bg-ink-800'
            }`}
          >
            <span aria-hidden="true">{option.icon}</span>
            <span className="ml-1.5">{option.label}</span>
          </button>
        );
      })}
    </div>
  );
}
