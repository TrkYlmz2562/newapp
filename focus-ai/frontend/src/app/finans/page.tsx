'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ErrorState } from '@/components/Shell';
import { api, describeError } from '@/lib/api';
import {
  INSTRUMENT_LABELS,
  LIFECYCLE,
  TIER_HELP,
  TIER_LABELS,
  formatEventDate,
  lifecycleStep,
  timeUntil,
} from '@/lib/finance';
import { formatDate } from '@/lib/format';
import type { FinanceFeed, FinanceItem } from '@/lib/types';

type Tab = 'certain' | 'conditional';

export default function FinancePage() {
  const [tab, setTab] = useState<Tab>('certain');
  const [feed, setFeed] = useState<FinanceFeed | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    api.finance
      .feed(tab === 'conditional')
      .then((result) => {
        if (!cancelled) setFeed(result);
      })
      .catch((err) => {
        if (!cancelled) setError(describeError(err, 'Finans akışı yüklenemedi.'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [tab]);

  const conditional = tab === 'conditional';
  const items = feed ? [...feed.realized, ...feed.soon, ...feed.later] : [];

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <h1 className="font-serif text-2xl font-semibold tracking-tight text-ink-900 dark:text-ink-50 sm:text-3xl">
          Finans
        </h1>

        {/*
          Non-dismissible by design. The section's whole claim is a narrow one —
          "this was decided", not "this will happen" — and a reader who misses that
          distinction is the one this feature could actually cost money.
        */}
        <p className="rounded-xl border border-ink-200 bg-ink-50 p-3 text-[13px] leading-relaxed text-ink-600 dark:border-ink-800 dark:bg-ink-900 dark:text-ink-300">
          Bu bölüm <strong className="font-semibold">yatırım tavsiyesi içermez.</strong> Burada yalnızca
          resmî belgeye, imzalı anlaşmaya veya yetkili merciin kendi açıklamasına dayanan gelişmeler
          listelenir. Bu liste bir gelişmenin <em>olacağını</em> değil,{' '}
          <em>resmen karara bağlandığını</em> gösterir. Karara bağlanmış bir işlem de iptal edilebilir,
          ertelenebilir veya değiştirilebilir.
        </p>
      </header>

      <div className="flex gap-2" role="tablist" aria-label="Finans görünümü">
        <TabButton active={!conditional} onClick={() => setTab('certain')}>
          Kesinleşenler
        </TabButton>
        <TabButton active={conditional} onClick={() => setTab('conditional')}>
          Şarta bağlı
        </TabButton>
      </div>

      {conditional && (
        <p className="rounded-xl border-l-4 border-signal-caution bg-signal-caution/5 p-3 text-[13px] text-ink-700 dark:text-ink-200">
          Bu sekmedeki gelişmeler <strong className="font-semibold">henüz kesinleşmemiştir.</strong> Bir
          onaya veya şarta bağlıdır ve gerçekleşmeyebilir.
        </p>
      )}

      {error && <ErrorState message={error} />}

      {loading && <p className="text-sm text-ink-500 dark:text-ink-400">Yükleniyor…</p>}

      {!loading && !error && items.length === 0 && (
        <div className="card p-6 text-sm text-ink-600 dark:text-ink-300">
          <p className="font-medium text-ink-900 dark:text-ink-50">Henüz gösterilecek bir gelişme yok.</p>
          <p className="mt-2">
            {conditional
              ? 'Şu an adı konmuş bir onayı bekleyen bir gelişme bulunmuyor.'
              : 'Bu bölüme yalnızca resmî belgeye dayanan gelişmeler girer; eşiği geçen bir haber henüz yok.'}{' '}
            Bir gelişmenin burada olmaması, o gelişmenin yaşanmadığı anlamına gelmez.
          </p>
        </div>
      )}

      {!loading && !error && feed && (
        <>
          <Section title="Yakında ve kesinleşmiş" items={feed.soon} />
          <Section title="İleri tarihli ve kesinleşmiş" items={feed.later} />
          <Section title="Son gerçekleşenler" items={feed.realized} muted />
        </>
      )}

      <footer className="border-t border-ink-200 pt-5 text-[11.5px] leading-relaxed text-ink-500 dark:border-ink-800 dark:text-ink-400">
        <p className="font-semibold uppercase tracking-wide">Yasal uyarı</p>
        <p className="mt-2">
          Burada yer alan bilgi, yorum ve değerlendirmeler yatırım danışmanlığı kapsamında değildir.
          Yatırım danışmanlığı hizmeti, yetkili kuruluşlar tarafından kişilerin risk ve getiri
          tercihleri dikkate alınarak kişiye özel sunulmaktadır. Burada yer alan içerikler genel
          niteliktedir ve mali durumunuz ile risk ve getiri tercihlerinize uygun olmayabilir. Bu
          nedenle, sadece burada yer alan bilgilere dayanılarak yatırım kararı verilmesi
          beklentilerinize uygun sonuçlar doğurmayabilir. Focus AI bir haber derleme hizmetidir; alım,
          satım veya elde tutma yönünde herhangi bir tavsiyede bulunmaz.
        </p>
      </footer>
    </div>
  );
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={`rounded-full px-4 py-1.5 text-sm font-semibold transition ${
        active
          ? 'bg-ink-900 text-white dark:bg-ink-100 dark:text-ink-900'
          : 'border border-ink-200 text-ink-600 hover:border-focus-400 dark:border-ink-700 dark:text-ink-300'
      }`}
    >
      {children}
    </button>
  );
}

function Section({ title, items, muted }: { title: string; items: FinanceItem[]; muted?: boolean }) {
  if (items.length === 0) return null;

  return (
    <section className="space-y-3">
      <h2 className="font-mono text-[11px] font-bold uppercase tracking-[0.14em] text-ink-500 dark:text-ink-400">
        {title}
      </h2>
      <div className="space-y-3">
        {items.map((item) => (
          <FinanceCard key={item.id} item={item} muted={muted} />
        ))}
      </div>
    </section>
  );
}

/**
 * Shows what makes an item checkable, not how likely it is: the tier's name, the
 * document behind it, and the sentence the classification came from. The citation
 * is the product — so it is typeset as prominently as the headline.
 */
function FinanceCard({ item, muted }: { item: FinanceItem; muted?: boolean }) {
  const step = lifecycleStep(item.tier);
  const eventDate = formatEventDate(item);
  const countdown = timeUntil(item.eventDate);
  const instrument = INSTRUMENT_LABELS[item.instrument];

  return (
    <article className={`card p-4 sm:p-5 ${muted ? 'opacity-80' : ''}`}>
      <div className="flex flex-wrap items-center gap-2">
        <span className="chip bg-ink-100 font-semibold text-ink-700 dark:bg-ink-800 dark:text-ink-200">
          {TIER_LABELS[item.tier]}
        </span>
        <span className="text-[11.5px] text-ink-500 dark:text-ink-400">{TIER_HELP[item.tier]}</span>
      </div>

      <Link href={`/story/${item.slug}`} className="mt-2 block">
        <h3 className="font-serif text-lg font-semibold leading-snug text-ink-900 dark:text-ink-50">
          {item.title}
        </h3>
      </Link>

      {item.event && (
        <p className="mt-1.5 text-sm text-ink-600 dark:text-ink-300">{item.event}</p>
      )}

      {/* A stepper, not a percentage: every step is an event someone can verify. */}
      {step >= 0 && (
        <ol className="mt-3 flex items-center gap-1.5" aria-label="Durum">
          {LIFECYCLE.map((label, index) => (
            <li key={label} className="flex flex-1 flex-col gap-1">
              <span
                className={`h-1 rounded-full ${
                  index <= step ? 'bg-ink-800 dark:bg-ink-100' : 'bg-ink-200 dark:bg-ink-800'
                }`}
              />
              <span
                className={`font-mono text-[9.5px] uppercase tracking-wide ${
                  index === step
                    ? 'font-bold text-ink-800 dark:text-ink-100'
                    : 'text-ink-400 dark:text-ink-500'
                }`}
              >
                {label}
              </span>
            </li>
          ))}
        </ol>
      )}

      {item.quote && (
        // Quoting is the anti-fabrication mechanism made visible: the reader can
        // check the classification against the source's own sentence.
        <blockquote className="mt-3 border-l-2 border-ink-300 pl-3 font-serif text-sm italic leading-relaxed text-ink-700 dark:border-ink-700 dark:text-ink-200">
          “{item.quote}”
        </blockquote>
      )}

      {item.condition && (
        <p className="mt-3 rounded-lg bg-signal-caution/10 px-3 py-2 text-[13px] text-ink-800 dark:text-ink-100">
          <span className="font-semibold">Şarta bağlı: </span>
          {item.condition} alınmadan yürürlüğe girmez.
        </p>
      )}

      <dl className="mt-3 space-y-1 border-t border-ink-200/70 pt-3 font-mono text-[11.5px] text-ink-500 dark:border-ink-800 dark:text-ink-400">
        {(item.reference || instrument) && (
          <div className="flex gap-2">
            <dt className="shrink-0 font-semibold">Dayanak:</dt>
            <dd>{item.reference ?? instrument}</dd>
          </div>
        )}
        {eventDate && (
          <div className="flex gap-2">
            <dt className="shrink-0 font-semibold">Tarih:</dt>
            <dd>
              {eventDate}
              {countdown && <span className="text-ink-400 dark:text-ink-500"> · {countdown}</span>}
            </dd>
          </div>
        )}
        <div className="flex gap-2">
          <dt className="shrink-0 font-semibold">Kaynak:</dt>
          <dd>
            {item.sourceCount} kaynak · {formatDate(item.publishedAt)}
          </dd>
        </div>
      </dl>
    </article>
  );
}
