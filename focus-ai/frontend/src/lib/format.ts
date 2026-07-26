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
};

/**
 * Two-stop gradients per category, used to draw a generated illustration when a
 * story has no real image. Kept as explicit hex pairs (not Tailwind classes) so
 * they render via inline styles — no purge surprises — and read well on both the
 * light and dark card, since the tile is always a saturated colour with white marks.
 */
export const CATEGORY_ART: Record<ContentCategory, [string, string]> = {
  Unknown: ['#64748b', '#334155'],
  Ai: ['#7c3aed', '#c026d3'],
  Software: ['#2563eb', '#1636e1'],
  OpenSource: ['#059669', '#0f766e'],
  Startup: ['#f97316', '#e11d48'],
  Science: ['#0891b2', '#4338ca'],
  Career: ['#0d9488', '#0369a1'],
  Tools: ['#d97706', '#b45309'],
  Security: ['#e11d48', '#881337'],
  Hardware: ['#475569', '#1e293b'],
  Product: ['#db2777', '#9333ea'],
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
