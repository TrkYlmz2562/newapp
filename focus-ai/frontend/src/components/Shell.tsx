'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';

export function PageHeader({
  title,
  subtitle,
  action,
}: {
  title: string;
  subtitle?: string;
  action?: React.ReactNode;
}) {
  return (
    <header className="flex items-start justify-between gap-4 px-4 pb-4 pt-6 sm:px-5">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-ink-900 dark:text-ink-50">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-ink-500 dark:text-ink-400">{subtitle}</p>}
      </div>
      {action}
    </header>
  );
}

/** Top search field. Submits to /search, which owns the results view. */
export function SearchBar({ initialQuery = '' }: { initialQuery?: string }) {
  const router = useRouter();
  const [value, setValue] = useState(initialQuery);

  return (
    <form
      role="search"
      className="px-4 sm:px-5"
      onSubmit={(event) => {
        event.preventDefault();
        const trimmed = value.trim();
        if (trimmed) router.push(`/search?q=${encodeURIComponent(trimmed)}`);
      }}
    >
      <div className="relative">
        <svg
          className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          aria-hidden="true"
        >
          <circle cx="11" cy="11" r="7" />
          <path strokeLinecap="round" d="m20 20-3.5-3.5" />
        </svg>
        <input
          type="search"
          name="q"
          value={value}
          onChange={(event) => setValue(event.target.value)}
          placeholder="Örn: son bir ayda çıkan AI Agent haberleri"
          aria-label="Haberlerde ara"
          className="input pl-10"
        />
      </div>
    </form>
  );
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="card mx-4 flex flex-col items-center gap-3 p-8 text-center sm:mx-5">
      <span aria-hidden="true" className="text-3xl">
        🌱
      </span>
      <h2 className="text-base font-semibold text-ink-800 dark:text-ink-100">{title}</h2>
      <p className="max-w-sm text-sm text-ink-500 dark:text-ink-400">{description}</p>
      {action}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div
      role="alert"
      className="mx-4 rounded-2xl border border-rose-200 bg-rose-50 p-5 text-sm text-rose-800
                 dark:border-rose-900 dark:bg-rose-950/50 dark:text-rose-200 sm:mx-5"
    >
      <p className="font-medium">{message}</p>
      {onRetry && (
        <button type="button" onClick={onRetry} className="btn-ghost mt-3">
          Tekrar dene
        </button>
      )}
    </div>
  );
}

export function SignInPrompt({ message }: { message: string }) {
  return (
    <EmptyState
      title="Giriş yapman gerekiyor"
      description={message}
      action={
        <div className="flex gap-2">
          <Link href="/login" className="btn-primary">
            Giriş yap
          </Link>
          <Link href="/register" className="btn-ghost">
            Hesap oluştur
          </Link>
        </div>
      }
    />
  );
}
