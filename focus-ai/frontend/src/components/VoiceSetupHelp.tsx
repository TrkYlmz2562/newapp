'use client';

import { useEffect, useState } from 'react';
import { speech } from '@/lib/speech';
import { trUpper } from '@/lib/format';

/**
 * What to do when the device has no Turkish voice.
 *
 * The alternative — reading Turkish with an English voice — is not a degraded
 * experience, it is an unusable one: the vowels are wrong, the suffixes come out
 * as noise, and a listener cannot follow a sentence. So playback is withheld and
 * the reader is told exactly where the setting lives on their own device, which
 * is a two-minute fix and a permanent one.
 *
 * Steps are per platform because the path differs on each, and "look in your
 * settings" is not help.
 */

type Platform = 'ios' | 'android' | 'macos' | 'windows' | 'unknown';

const STEPS: Record<Platform, { label: string; path: string[] }> = {
  ios: {
    label: 'iPhone / iPad',
    path: ['Ayarlar', 'Erişilebilirlik', 'Sözlü İçerik', 'Sesler', 'Türkçe', 'bir ses indir'],
  },
  android: {
    label: 'Android',
    path: ['Ayarlar', 'Sistem', 'Diller ve giriş', 'Metin okuma çıkışı', 'Dil yükle', 'Türkçe'],
  },
  macos: {
    label: 'Mac',
    path: ['Sistem Ayarları', 'Erişilebilirlik', 'Sözlü İçerik', 'Sistem sesi', 'Sesi Yönet', 'Türkçe'],
  },
  windows: {
    label: 'Windows',
    path: ['Ayarlar', 'Saat ve dil', 'Dil ve bölge', 'Türkçe ekle', 'Konuşma özelliğini işaretle'],
  },
  unknown: {
    label: 'Cihazın',
    path: ['Sistem ayarları', 'Erişilebilirlik veya Dil', 'Metin okuma / Sözlü içerik', 'Türkçe ses ekle'],
  },
};

function detect(): Platform {
  if (typeof navigator === 'undefined') return 'unknown';

  const agent = navigator.userAgent;

  // iPadOS reports itself as a Mac; the touch points are what separate them.
  if (/iPhone|iPod/.test(agent)) return 'ios';
  if (/iPad/.test(agent) || (/Macintosh/.test(agent) && navigator.maxTouchPoints > 1)) return 'ios';
  if (/Android/.test(agent)) return 'android';
  if (/Macintosh/.test(agent)) return 'macos';
  if (/Windows/.test(agent)) return 'windows';

  return 'unknown';
}

export function VoiceSetupHelp({ className = '' }: { className?: string }) {
  const [platform, setPlatform] = useState<Platform>('unknown');
  const [checking, setChecking] = useState(false);

  // After hydration: the user agent is not available during server rendering, and
  // guessing would render the wrong instructions for one frame.
  useEffect(() => setPlatform(detect()), []);

  const steps = STEPS[platform];

  const recheck = () => {
    setChecking(true);
    speech.refreshVoices();

    // The engine flips `ready` back on by itself; this only clears the button's
    // own busy state once the poll window has had a chance to run.
    setTimeout(() => setChecking(false), 3000);
  };

  return (
    <section className={`card p-4 ${className}`} aria-label="Sesli okuma kurulumu">
      <p className="font-mono text-[11px] tracking-[0.13em] text-ink-500 dark:text-ink-400">
        {trUpper('Sesli okuma')}
      </p>

      <p className="mt-1.5 text-[13px] leading-relaxed text-ink-600 dark:text-ink-300">
        Bu cihazda Türkçe ses yüklü değil. Türkçe metni İngilizce sesle okutmak
        anlaşılmaz çıktı verdiği için oynatmayı kapattım — iki dakikalık bir kurulumla açılır.
      </p>

      <p className="mt-3 font-mono text-[11px] tracking-[0.13em] text-focus-600 dark:text-focus-400">
        {trUpper(steps.label)}
      </p>

      <ol className="mt-1 flex flex-wrap items-center gap-x-1.5 gap-y-1 text-[13px] text-ink-700 dark:text-ink-200">
        {steps.path.map((step, index) => (
          <li key={step} className="flex items-center gap-1.5">
            {index > 0 && (
              <span aria-hidden="true" className="text-ink-400 dark:text-ink-600">
                ›
              </span>
            )}
            <span>{step}</span>
          </li>
        ))}
      </ol>

      <button type="button" onClick={recheck} disabled={checking} className="btn-ghost mt-3 w-full">
        {checking ? 'Bakılıyor…' : 'Kurdum, tekrar bak'}
      </button>
    </section>
  );
}
