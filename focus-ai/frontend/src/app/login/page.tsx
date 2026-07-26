'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { describeError } from '@/lib/api';

export default function LoginPage() {
  const router = useRouter();
  const { login } = useAuth();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (busy) return;

    setBusy(true);
    setError(null);

    try {
      await login(email, password);
      router.push('/');
    } catch (caught) {
      setError(describeError(caught, 'Giriş yapılamadı.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center px-5 py-10">
      <h1 className="text-2xl font-bold text-ink-900 dark:text-ink-50">Focus AI&apos;a giriş yap</h1>
      <p className="mt-1 text-sm text-ink-500 dark:text-ink-400">
        Günün önemli gelişmeleri, ilgi alanlarına göre sıralanmış.
      </p>

      <form onSubmit={submit} className="mt-6 space-y-3">
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
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className="input"
          />
        </div>

        {error && (
          <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
            {error}
          </p>
        )}

        <button type="submit" disabled={busy} className="btn-primary w-full">
          {busy ? 'Giriş yapılıyor…' : 'Giriş yap'}
        </button>
      </form>

      <p className="mt-6 text-center text-sm text-ink-500 dark:text-ink-400">
        Hesabın yok mu?{' '}
        <Link href="/register" className="font-medium text-focus-600 dark:text-focus-400">
          Kayıt ol
        </Link>
      </p>
    </div>
  );
}
