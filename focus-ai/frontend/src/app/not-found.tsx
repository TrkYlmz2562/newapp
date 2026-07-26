import Link from 'next/link';

export default function NotFound() {
  return (
    <div className="mx-auto flex min-h-dvh max-w-sm flex-col items-center justify-center gap-3 px-5 text-center">
      <span aria-hidden="true" className="text-4xl">
        🧭
      </span>
      <h1 className="text-xl font-bold text-ink-900 dark:text-ink-50">Sayfa bulunamadı</h1>
      <p className="text-sm text-ink-500 dark:text-ink-400">
        Aradığın haber kaldırılmış ya da bağlantı hatalı olabilir.
      </p>
      <Link href="/" className="btn-primary mt-2">
        Ana sayfaya dön
      </Link>
    </div>
  );
}
