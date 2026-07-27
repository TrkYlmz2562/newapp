'use client';

import { useEffect, useState } from 'react';
import { CopyButton } from '@/components/CopyButton';
import { api } from '@/lib/api';
import type { MentorPersona } from '@/lib/types';

/**
 * The one-time setup: paste Focus Mentor into a Claude Project.
 *
 * Done once, every brief afterwards is short — the teacher is already loaded on
 * the other side and only the case file travels. The alternative is carrying the
 * persona in every single prompt, which works but adds a few kilobytes to each
 * paste and makes the reader re-read the same instructions forever.
 *
 * The text is fetched rather than duplicated here so the app and the model
 * planner cannot drift into teaching to two different sets of rules.
 */
export function MentorSetup() {
  const [persona, setPersona] = useState<MentorPersona | null>(null);
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    void api.learning.persona().then(setPersona).catch(() => setPersona(null));
  }, []);

  return (
    <section id="mentor" className="card scroll-mt-4 p-5">
      <h2 className="mb-1 text-sm font-semibold text-ink-800 dark:text-ink-100">
        Focus Mentor kurulumu
      </h2>

      <p className="mb-3 text-xs leading-relaxed text-ink-500 dark:text-ink-400">
        Öğren sekmesindeki brifler, Claude'da bu personayla çalışmak üzere yazılıyor. Bir kez
        kurarsan her brifte yalnızca dersin kendisini yapıştırırsın.
      </p>

      <ol className="mb-3 space-y-1.5 text-xs leading-relaxed text-ink-600 dark:text-ink-300">
        <li>
          <span className="font-mono text-[11px] text-focus-600 dark:text-focus-400">1.</span>{' '}
          claude.ai'da yeni bir Proje aç, adını <strong>Focus Mentor</strong> koy.
        </li>
        <li>
          <span className="font-mono text-[11px] text-focus-600 dark:text-focus-400">2.</span>{' '}
          Aşağıdaki metni projenin talimat alanına yapıştır.
        </li>
        <li>
          <span className="font-mono text-[11px] text-focus-600 dark:text-focus-400">3.</span>{' '}
          Bundan sonra her brifi bu projeye mesaj olarak gönder.
        </li>
      </ol>

      {persona ? (
        <>
          <CopyButton text={persona.text} label="Persona metnini kopyala" variant="ghost" />

          <button
            type="button"
            onClick={() => setExpanded((current) => !current)}
            aria-expanded={expanded}
            className="tap-row mt-2 font-mono text-[11px] tracking-wide text-ink-500 hover:text-ink-800
                       dark:text-ink-400 dark:hover:text-ink-200"
          >
            {expanded ? '▾ metni gizle' : '▸ metni oku'}
          </button>

          {expanded && (
            <textarea
              readOnly
              value={persona.text}
              rows={16}
              onFocus={(event) => event.currentTarget.select()}
              className="mt-2 w-full rounded-xl border border-ink-200 bg-ink-50 p-3 font-mono text-[12px]
                         leading-relaxed text-ink-800 dark:border-ink-800 dark:bg-ink-950 dark:text-ink-200"
            />
          )}
        </>
      ) : (
        <div className="skeleton h-10 w-full" />
      )}
    </section>
  );
}
