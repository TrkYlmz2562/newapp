'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { api, describeError } from '@/lib/api';
import type { Topic } from '@/lib/types';

export default function RegisterPage() {
  const router = useRouter();
  const { register } = useAuth();

  const [step, setStep] = useState<'account' | 'interests'>('account');
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [topics, setTopics] = useState<Topic[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    // Preloaded so step two renders instantly once the account form is done.
    api.topics().then(setTopics).catch(() => setTopics([]));
  }, []);

  const submit = async () => {
    if (busy) return;

    setBusy(true);
    setError(null);

    try {
      await register({ email, password, displayName, interestSlugs: [...selected] });
      router.push('/');
    } catch (caught) {
      // Back to the account step so the user can fix the offending field —
      // and now the message names which field (e.g. "Şifre en az 8 karakter olmalı.").
      setError(describeError(caught, 'Kayıt tamamlanamadı.'));
      setStep('account');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center px-5 py-10">
      {step === 'account' ? (
        <>
          <h1 className="text-2xl font-bold text-ink-900 dark:text-ink-50">Hesap oluştur</h1>
          <p className="mt-1 text-sm text-ink-500 dark:text-ink-400">
            Sosyal medya yerine günde 5 dakika.
          </p>

          <form
            className="mt-6 space-y-3"
            onSubmit={(event) => {
              event.preventDefault();
              setError(null);
              setStep('interests');
            }}
          >
            <div>
              <label htmlFor="name" className="mb-1 block text-sm font-medium">
                Ad
              </label>
              <input
                id="name"
                required
                maxLength={80}
                autoComplete="name"
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
                className="input"
              />
            </div>

            <div>
              <label htmlFor="email" className="mb-1 block text-sm font-medium">
                E-posta
              </label>
              <input
                id="email"
                type="email"
                required
                autoComplete="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                className="input"
              />
            </div>

            <div>
              <label htmlFor="password" className="mb-1 block text-sm font-medium">
                Şifre
              </label>
              <input
                id="password"
                type="password"
                required
                minLength={8}
                autoComplete="new-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                className="input"
              />
              <p className="mt-1 text-xs text-ink-400">En az 8 karakter.</p>
            </div>

            {error && (
              <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
                {error}
              </p>
            )}

            <button type="submit" className="btn-primary w-full">
              Devam et
            </button>
          </form>

          <p className="mt-6 text-center text-sm text-ink-500 dark:text-ink-400">
            Zaten hesabın var mı?{' '}
            <Link href="/login" className="font-medium text-focus-600 dark:text-focus-400">
              Giriş yap
            </Link>
          </p>
        </>
      ) : (
        <>
          <h1 className="text-2xl font-bold text-ink-900 dark:text-ink-50">Neyle ilgileniyorsun?</h1>
          <p className="mt-1 text-sm text-ink-500 dark:text-ink-400">
            Seçtiklerin ilk günden itibaren akışını şekillendirir. Sonradan değiştirebilirsin.
          </p>

          <div className="mt-3 flex items-center gap-3">
            <button
              type="button"
              onClick={() =>
                setSelected((current) =>
                  current.size === topics.length && topics.length > 0
                    ? new Set()
                    : new Set(topics.map((t) => t.slug)),
                )
              }
              className="text-sm font-medium text-focus-600 dark:text-focus-400"
            >
              {selected.size === topics.length && topics.length > 0 ? 'Tümünü kaldır' : 'Tümünü seç'}
            </button>
            <span className="text-xs text-ink-500 dark:text-ink-400">
              {selected.size}/{topics.length} seçili
            </span>
          </div>

          <div className="mt-3 flex max-h-[50vh] flex-wrap gap-2 overflow-y-auto">
            {topics.map((topic) => {
              const active = selected.has(topic.slug);
              return (
                <button
                  key={topic.id}
                  type="button"
                  aria-pressed={active}
                  onClick={() =>
                    setSelected((current) => {
                      const next = new Set(current);
                      if (next.has(topic.slug)) next.delete(topic.slug);
                      else next.add(topic.slug);
                      return next;
                    })
                  }
                  className={`chip border px-3 py-1.5 text-sm transition ${
                    active
                      ? 'border-focus-500 bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200'
                      : 'border-ink-200 bg-white text-ink-600 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-300'
                  }`}
                >
                  {topic.name}
                </button>
              );
            })}
          </div>

          {error && (
            <p role="alert" className="mt-3 text-sm text-rose-600 dark:text-rose-400">
              {error}
            </p>
          )}

          <div className="mt-5 space-y-2">
            <button type="button" onClick={submit} disabled={busy} className="btn-primary w-full">
              {busy ? 'Hesap oluşturuluyor…' : `Başla${selected.size > 0 ? ` (${selected.size} seçim)` : ''}`}
            </button>
            <button type="button" onClick={() => setStep('account')} className="btn-ghost w-full">
              Geri
            </button>
          </div>
        </>
      )}
    </div>
  );
}
