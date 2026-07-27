'use client';

import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '@/components/AuthProvider';
import { CopyButton } from '@/components/CopyButton';
import { ErrorState, PageHeader, SignInPrompt } from '@/components/Shell';
import { api, describeError } from '@/lib/api';
import { trUpper } from '@/lib/format';
import type { LearningBrief, MentorPersona } from '@/lib/types';

/**
 * One lesson brief: the prompt, and the two ways to take it to Claude.
 *
 * There are two copy buttons because there are two situations. If the reader has
 * set Focus Mentor up as a Claude Project once — which is the recommended path,
 * see /profile#mentor — the persona is already loaded there and the prompt alone
 * is what gets pasted. If they are starting an ordinary chat instead, the
 * persona has to travel with it. Same brief, different envelope.
 */
export default function BriefPage() {
  const params = useParams<{ id: string }>();
  const { user, loading: authLoading } = useAuth();

  const [brief, setBrief] = useState<LearningBrief | null>(null);
  const [persona, setPersona] = useState<MentorPersona | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showRaw, setShowRaw] = useState(false);

  const load = useCallback(async () => {
    if (!user || !params?.id) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const [briefResult, personaResult] = await Promise.all([
        api.learning.briefs.get(params.id),
        api.learning.persona().catch(() => null),
      ]);

      setBrief(briefResult);
      setPersona(personaResult);
    } catch (caught) {
      setError(describeError(caught, 'Bu brif yüklenemedi.'));
    } finally {
      setLoading(false);
    }
  }, [user, params?.id]);

  useEffect(() => {
    if (!authLoading) void load();
  }, [authLoading, load]);

  const generate = async () => {
    if (!brief) return;

    setBusy(true);
    setError(null);

    try {
      setBrief(await api.learning.briefs.generate(brief.id));
    } catch (caught) {
      setError(describeError(caught, 'Prompt üretilemedi.'));
    } finally {
      setBusy(false);
    }
  };

  const markDone = async () => {
    if (!brief) return;

    const previous = brief.status;
    setBrief({ ...brief, status: 'Done' });

    try {
      await api.learning.briefs.setStatus(brief.id, 'Done');
    } catch {
      setBrief({ ...brief, status: previous });
    }
  };

  if (!authLoading && !user) {
    return (
      <div className="space-y-4">
        <PageHeader title="Ders brifi" />
        <SignInPrompt message="Brifler hesabına bağlı, bu yüzden giriş gerekiyor." />
      </div>
    );
  }

  if (loading || authLoading) {
    return (
      <div className="space-y-4 px-4 pt-6 sm:px-5">
        <div className="skeleton h-4 w-24" />
        <div className="skeleton h-7 w-3/4" />
        <div className="skeleton h-40 w-full" />
      </div>
    );
  }

  if (error && !brief) {
    return (
      <div className="space-y-4">
        <PageHeader title="Ders brifi" />
        <ErrorState message={error} onRetry={load} />
      </div>
    );
  }

  if (!brief) return null;

  const prompt = brief.prompt ?? '';
  const withPersona = persona ? `${persona.text}\n\n---\n\n${prompt}` : prompt;

  return (
    <div className="space-y-4 pb-4">
      <div className="px-4 pt-6 sm:px-5">
        <Link
          href="/learning"
          className="tap-row font-mono text-[11px] tracking-[0.13em] text-ink-500 hover:text-ink-800
                     dark:text-ink-400 dark:hover:text-ink-200"
        >
          {trUpper('‹ Öğren')}
        </Link>

        {/* Joined rather than each part carrying its own separator: the entry
            level is absent whenever the planner did not run, and a hardcoded
            leading "·" then hangs on its own. */}
        <div className="mt-3 flex flex-wrap items-center gap-x-1.5 gap-y-1 font-mono text-[11px] text-ink-500 dark:text-ink-400">
          {[
            brief.entryLabel && (
              <span key="level" className="text-focus-600 dark:text-focus-400">
                {brief.entryLabel}
              </span>
            ),
            brief.stories.length > 0 && <span key="count">{brief.stories.length} haber</span>,
            brief.status === 'Done' && (
              <span key="done" className="text-signal-trust">
                ✓ çalışıldı
              </span>
            ),
          ]
            .filter(Boolean)
            .map((part, index) => (
              <span key={index} className="flex items-center gap-1.5">
                {index > 0 && <span aria-hidden="true">·</span>}
                {part}
              </span>
            ))}
        </div>

        <h1 className="mt-1.5 font-serif text-2xl font-semibold leading-tight tracking-tight text-ink-900 dark:text-ink-50">
          {brief.title}
        </h1>

        {brief.learningGoal && (
          <p className="mt-2 font-serif text-[15px] leading-relaxed text-ink-600 dark:text-ink-300">
            {brief.learningGoal}
          </p>
        )}
      </div>

      <div className="space-y-4 px-4 sm:px-5">
        {error && <p className="text-sm text-signal-hype">{error}</p>}

        {brief.status === 'Queued' ? (
          <section className="card space-y-3 p-5">
            <p className="font-serif text-sm leading-relaxed text-ink-700 dark:text-ink-300">
              Bu ders sırada. Prompt henüz üretilmedi — üretmek tek bir model çağrısı harcar ve
              sonuç kaydedilir, bir daha üretilmez.
            </p>
            <button type="button" onClick={generate} disabled={busy} className="btn-primary w-full">
              {busy ? 'Üretiliyor…' : 'Prompt üret'}
            </button>
          </section>
        ) : (
          <>
            {brief.plannerUnavailable && (
              <p className="rounded-xl border border-signal-caution/40 bg-signal-caution/10 p-3 text-[13px] text-signal-caution">
                Çapa sorular üretilemedi — plan modeli yanıt vermedi. Dosya yine de eksiksiz:
                kaynaklar, alıntılar ve kanıt kaydı yerinde.
              </p>
            )}

            <div className="flex flex-col gap-3 sm:flex-row">
              <CopyButton
                text={prompt}
                label="Promtu kopyala"
                hint="Focus Mentor projesine yapıştır"
                onFailed={() => setShowRaw(true)}
              />
              <CopyButton
                text={withPersona}
                label="Persona ile kopyala"
                hint="Boş bir sohbete yapıştır"
                variant="ghost"
                onFailed={() => setShowRaw(true)}
              />
            </div>

            <p className="text-center text-[11.5px] text-ink-500 dark:text-ink-400">
              Mentor'u bir kez Claude Projesi olarak kurarsan ilk düğme yeter —{' '}
              <Link href="/profile#mentor" className="text-focus-600 underline underline-offset-2 dark:text-focus-400">
                kurulum
              </Link>
            </p>

            {brief.demoIdea && (
              <section className="card p-4">
                <p className="font-mono text-[11px] tracking-[0.13em] text-focus-600 dark:text-focus-400">
                  {trUpper('Mentor bunu kuracak')}
                </p>
                <p className="mt-1.5 font-serif text-sm leading-relaxed text-ink-700 dark:text-ink-300">
                  {brief.demoIdea}
                </p>
              </section>
            )}

            {/*
              The prompt is shown, not hidden behind the button. Someone about to
              paste a few kilobytes into a chat is entitled to read it first, and
              when the clipboard is unavailable this block is the only way to get
              the text at all — which is why it is a selectable textarea rather
              than a <pre>.
            */}
            <section className="space-y-2">
              <button
                type="button"
                onClick={() => setShowRaw((current) => !current)}
                aria-expanded={showRaw}
                className="tap-row font-mono text-[11px] tracking-[0.13em] text-ink-500 hover:text-ink-800
                           dark:text-ink-400 dark:hover:text-ink-200"
              >
                {trUpper(showRaw ? '▾ promtu gizle' : '▸ promtu göster')}
              </button>

              {showRaw && (
                <textarea
                  readOnly
                  value={prompt}
                  rows={18}
                  onFocus={(event) => event.currentTarget.select()}
                  className="w-full rounded-xl border border-ink-200 bg-ink-50 p-3 font-mono text-[12px]
                             leading-relaxed text-ink-800 dark:border-ink-800 dark:bg-ink-950 dark:text-ink-200"
                />
              )}
            </section>

            {brief.stories.length > 0 && (
              <section className="space-y-1.5">
                <p className="font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
                  {trUpper('Dayandığı haberler')}
                </p>
                {brief.stories.map((story) => (
                  <Link
                    key={story.storyId}
                    href={`/story/${story.slug}`}
                    className="tap-row block truncate rounded-lg px-2 py-1.5 text-sm text-focus-700 transition
                               hover:bg-ink-50 dark:text-focus-300 dark:hover:bg-ink-800"
                  >
                    {story.title}
                  </Link>
                ))}
              </section>
            )}

            {brief.status !== 'Done' && (
              <button type="button" onClick={markDone} className="btn-ghost w-full">
                Bu dersi çalıştım
              </button>
            )}
          </>
        )}
      </div>
    </div>
  );
}
