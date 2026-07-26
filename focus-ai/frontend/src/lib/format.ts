import type { ContentCategory, HypeLevel, LearnUrgency, LongevityOutlook } from './types';

const TR = 'tr-TR';

/** Relative time in Turkish. Falls back to an absolute date past a week. */
export function timeAgo(iso: string): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '';

  const minutes = Math.round((Date.now() - then) / 60000);

  if (minutes < 1) return 'az önce';
  if (minutes < 60) return `${minutes} dk önce`;

  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} sa önce`;

  const days = Math.round(hours / 24);
  if (days < 7) return `${days} gün önce`;

  return new Date(iso).toLocaleDateString(TR, { day: 'numeric', month: 'long' });
}

export function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString(TR, { day: 'numeric', month: 'long', year: 'numeric' });
}

export function formatDayHeading(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString(TR, { weekday: 'long', day: 'numeric', month: 'long' });
}

export const CATEGORY_LABELS: Record<ContentCategory, string> = {
  Unknown: 'Genel',
  Ai: 'Yapay Zekâ',
  Software: 'Yazılım',
  OpenSource: 'Açık Kaynak',
  Startup: 'Startup',
  Science: 'Bilim',
  Career: 'Kariyer',
  Tools: 'Araçlar',
  Security: 'Güvenlik',
  Hardware: 'Donanım',
  Product: 'Ürün',
  Finance: 'Finans',
};

export const CATEGORY_EMOJI: Record<ContentCategory, string> = {
  Unknown: '📌',
  Ai: '🤖',
  Software: '💻',
  OpenSource: '🧩',
  Startup: '🚀',
  Science: '🔬',
  Career: '🧭',
  Tools: '🛠️',
  Security: '🔐',
  Hardware: '⚙️',
  Product: '📦',
  Finance: '🏦',
};

/**
 * One accent per category, in a light and a dark variant.
 *
 * Both are needed because the accent is used as ink on the card, not as a fill:
 * a mid-tone that reads well on white (#3f4b60) disappears on the dark surface,
 * and inverting programmatically muddies the hues. Kept as hex rather than
 * Tailwind classes so they can be handed to inline styles as CSS variables — no
 * purge surprises, and the theme still picks the right one via a `.dark` rule.
 */
export const CATEGORY_ACCENT: Record<ContentCategory, { light: string; dark: string }> = {
  Unknown: { light: '#4c5a74', dark: '#94a3b8' },
  Ai: { light: '#6d3bd8', dark: '#a78bfa' },
  Software: { light: '#1d4ed8', dark: '#60a5fa' },
  OpenSource: { light: '#047857', dark: '#34d399' },
  Startup: { light: '#c2410c', dark: '#fb923c' },
  Science: { light: '#0e7490', dark: '#38bdf8' },
  Career: { light: '#0f766e', dark: '#2dd4bf' },
  Tools: { light: '#b45309', dark: '#fbbf24' },
  Security: { light: '#be123c', dark: '#fb7185' },
  Hardware: { light: '#475569', dark: '#a3b3c9' },
  Product: { light: '#be185d', dark: '#f472b6' },
  Finance: { light: '#0f766e', dark: '#5eead4' },
};

/** Terminal-style path segment for the card's title bar: `focus:~/<slug>`. ASCII only. */
export const CATEGORY_SLUG: Record<ContentCategory, string> = {
  Unknown: 'genel',
  Ai: 'yapay-zeka',
  Software: 'yazilim',
  OpenSource: 'acik-kaynak',
  Startup: 'startup',
  Science: 'bilim',
  Career: 'kariyer',
  Tools: 'araclar',
  Security: 'guvenlik',
  Hardware: 'donanim',
  Product: 'urun',
  Finance: 'finans',
};

/**
 * Minimal line-icon markup per category (inner SVG for a 24×24 viewBox, stroke =
 * currentColor). Drawn on the illustration tag instead of an emoji, so the visual
 * reads as designed rather than childish.
 */
export const CATEGORY_ICON: Record<ContentCategory, string> = {
  Unknown: '<circle cx="12" cy="12" r="9"/><path d="M12 8h.01M11 11.5h1V16h1"/>',
  Ai: '<rect x="6" y="6" width="12" height="12" rx="2"/><circle cx="12" cy="12" r="2.4"/><path d="M9 3v3M15 3v3M9 18v3M15 18v3M3 9h3M3 15h3M18 9h3M18 15h3"/>',
  Software: '<path d="M9 8l-4 4 4 4M15 8l4 4-4 4M13.5 6l-3 12"/>',
  OpenSource: '<circle cx="6" cy="6" r="2.4"/><circle cx="6" cy="18" r="2.4"/><circle cx="18" cy="9" r="2.4"/><path d="M6 8.4v7.2M6 15.6c0-4.2 12-1.8 12-6.6"/>',
  Startup: '<path d="M4 16l5-5 4 4 7-7"/><path d="M16 8h4v4"/>',
  Science: '<path d="M9 3h6M10 3v5.5L5.2 17A2 2 0 007 20h10a2 2 0 001.8-3L14 8.5V3"/>',
  Career: '<circle cx="12" cy="12" r="9"/><path d="M15.5 8.5l-2 5-5 2 2-5z"/>',
  Tools: '<path d="M15 4a4 4 0 00-3.5 6L4.5 17 7 19.5l7-7A4 4 0 0015 4z"/>',
  Security: '<path d="M12 3l7 3v6c0 4.6-3 7.7-7 9-4-1.3-7-4.4-7-9V6z"/><path d="M9 12l2 2 4-4"/>',
  Hardware: '<rect x="6" y="6" width="12" height="12" rx="1.5"/><rect x="9.5" y="9.5" width="5" height="5" rx="1"/><path d="M10 3v3M14 3v3M10 18v3M14 18v3M3 10h3M3 14h3M18 10h3M18 14h3"/>',
  Product: '<path d="M12 3l8 4v10l-8 4-8-4V7z"/><path d="M4 7l8 4 8-4M12 11v10"/>',
  Finance: '<path d="M3 21h18M5 21V10M9 21V10M15 21V10M19 21V10M12 3l9 5H3z"/>',
};

export const HYPE_LABELS: Record<HypeLevel, string> = {
  Understated: 'Hak ettiğinden az konuşuluyor',
  Accurate: 'Gerçekçi ölçüde konuşuluyor',
  SlightlyOverhyped: 'Biraz abartılıyor',
  Overhyped: 'Abartılıyor',
};

export const URGENCY_LABELS: Record<LearnUrgency, string> = {
  Now: 'Şimdi öğren',
  ThisQuarter: 'Bu çeyrekte öğren',
  Watch: 'İzlemede tut',
  Skip: 'Şimdilik atla',
};

export const LONGEVITY_LABELS: Record<LongevityOutlook, string> = {
  Foundational: 'Temel teknoloji olma yolunda',
  Durable: 'Kalıcı görünüyor',
  Uncertain: 'Belirsiz',
  Fading: 'Sönümleniyor',
};

/**
 * Trust bands. The wording matters as much as the number: a bare score invites
 * false precision, so each band says what it means for the reader.
 */
export function trustBand(score: number): { label: string; tone: 'good' | 'ok' | 'warn' } {
  if (score >= 85) return { label: 'Çok güvenilir', tone: 'good' };
  if (score >= 70) return { label: 'Güvenilir', tone: 'good' };
  if (score >= 50) return { label: 'Makul', tone: 'ok' };
  if (score >= 30) return { label: 'Temkinli yaklaş', tone: 'warn' };
  return { label: 'Doğrulanmamış', tone: 'warn' };
}

export function readingTime(minutes: number): string {
  return `${Math.max(1, minutes)} dk okuma`;
}

export function pluralizeSources(count: number): string {
  return count <= 1 ? 'tek kaynak' : `${count} kaynak`;
}

export function formatPercent(value: number): string {
  const rounded = Math.round(value);
  return `${rounded > 0 ? '+' : ''}${rounded}%`;
}

/**
 * True when a secondary field says nothing the headline did not.
 *
 * When a feed carries no body text and no LLM is configured, the extractive
 * fallback legitimately returns the title as the summary. Rendering it anyway
 * shows the same sentence two or three times on one card, which reads as a bug
 * even though the data is honest.
 */
export function isRedundant(text: string | null | undefined, title: string): boolean {
  if (!text) return true;

  const strip = (value: string) =>
    value
      .toLocaleLowerCase('tr')
      .replace(/[^\p{L}\p{N}]+/gu, ' ')
      .trim();

  const a = strip(text);
  const b = strip(title);

  if (a.length === 0) return true;
  if (a === b) return true;

  // Also catch "<title>." and other trivial extensions of the headline.
  return a.startsWith(b) && a.length - b.length < 12;
}

/** Returns the field only when it adds something beyond the headline. */
export function meaningful(text: string | null | undefined, title: string): string | null {
  return isRedundant(text, title) ? null : (text as string);
}
