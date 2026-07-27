'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { api, describeError } from '@/lib/api';
import { trUpper } from '@/lib/format';

/**
 * "Öğrenme çıktısı üret" — the foot of the article, where the lesson is made.
 *
 * Placed at the end because that is when the decision is actually available: what
 * is worth studying about a story is something you know after reading it. The
 * spark in the action row at the top only parks it; this is the press that spends
 * a model call, which is why the two are different buttons in different places
 * rather than one button that quietly costs money.
 *
 * Queueing and generating are folded into the single press. Both are idempotent
 * server-side, so a story already parked from the top of the page is picked up
 * rather than queued twice, and a lesson already produced is opened rather than
 * regenerated.
 */
export function LearningOutput({
  storyId,
  briefId,
  onQueued,
}: {
  storyId: string;
  briefId: string | null;
  onQueued: (briefId: string) => void;
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async () => {
    setBusy(true);
    setError(null);

    try {
      let id = briefId;

      if (id === null) {
        const queued = await api.learning.briefs.queueStory(storyId);
        id = queued.id;
        onQueued(id);
      }

      // Returns the stored prompt untouched when there already is one, so this is
      // safe to press on a lesson that was generated days ago.
      await api.learning.briefs.generate(id);
      router.push(`/learning/brief/${id}`);
    } catch (caught) {
      setError(describeError(caught, 'Öğrenme çıktısı üretilemedi.'));
      setBusy(false);
    }
  };

  return (
    <section className="card p-4 sm:p-5">
      <p className="font-mono text-[11px] tracking-[0.13em] text-focus-600 dark:text-focus-400">
        {trUpper('Bu haberden ders çıkar')}
      </p>

      <p className="mt-1.5 font-serif text-sm leading-relaxed text-ink-600 dark:text-ink-300">
        Bu haberin kaynakları, alıntıları, kanıt kaydı ve kaynak bağlantıları Focus Mentor için
        tek bir ders dosyasına derlenir.
      </p>

      {error && <p className="mt-2 text-xs text-signal-hype">{error}</p>}

      <button type="button" onClick={run} disabled={busy} className="btn-primary mt-3 w-full">
        {busy ? 'Üretiliyor…' : 'Öğrenme çıktısı üret'}
      </button>

      {/* Said before the press, not after: this is the one control in the app that
          spends a model call, and the result is stored rather than re-derived. */}
      <p className="mt-2 text-center text-[11.5px] text-ink-500 dark:text-ink-400">
        Bir model çağrısı harcar, sonuç kaydedilir — ikinci kez üretilmez.
      </p>
    </section>
  );
}
