import {
  CATEGORY_ACCENT,
  CATEGORY_LABELS,
  CATEGORY_SLUG,
  HYPE_LABELS,
  LONGEVITY_LABELS,
  URGENCY_LABELS,
  trustBand,
} from './format';
import type { StoryDetail } from './types';

/**
 * Renders a whole story as one tall image — the entire read, top to bottom, meant
 * to be sent to someone who zooms in rather than follows a link.
 *
 * Deliberately does NOT draw the story's hero photo: a remote image taints the
 * canvas under the browser's origin rules and toBlob() then throws, so the export
 * would fail precisely on the stories with the best picture. The generated plate
 * is used instead — ours, always available, and already the app's language.
 *
 * Always dark, whatever theme the reader is using: the image is looked at inside
 * someone else's chat app, where being legible and consistent matters more than
 * matching our UI.
 */

const W = 1080;
const PAD = 64;
const BAR_H = 92;
const PLATE_H = 340;

/**
 * iOS Safari refuses canvases past roughly 16.7M pixels of area and returns a
 * blank export rather than an error. At this width that is ~15,500px tall; the
 * cap sits well below it, and sections that do not fit are dropped whole with a
 * visible note rather than cut mid-sentence.
 */
const MAX_H = 12_000;

const INK = '#eef2f8';
const MUTED = '#a9b4c6';
const FAINT = '#7e8da3';
const PAPER = '#0e141e';
const BAR = '#0c1017';
const RULE = 'rgba(255,255,255,0.12)';
const TRUST = '#34d39e';
const CAUTION = '#e0a53a';

const SERIF = 'Georgia, "Times New Roman", serif';
const MONO = 'ui-monospace, "SF Mono", Menlo, Consolas, monospace';
const SANS = 'system-ui, -apple-system, "Segoe UI", sans-serif';

type Op =
  | { kind: 'text'; text: string; font: string; fill: string; x: number; y: number; align?: CanvasTextAlign }
  | { kind: 'rule'; y: number }
  | { kind: 'bar'; x: number; y: number; w: number; fill: string };

/**
 * Shortens a string until it fits, marking the cut. Used for the footer URL,
 * which is right-aligned next to a left-aligned label: a long slug otherwise
 * runs straight through the label and off both edges of the image.
 */
function fit(ctx: CanvasRenderingContext2D, text: string, font: string, maxWidth: number): string {
  ctx.font = font;
  if (ctx.measureText(text).width <= maxWidth) {
    return text;
  }

  let cut = text;
  while (cut.length > 1 && ctx.measureText(`${cut}…`).width > maxWidth) {
    cut = cut.slice(0, -1);
  }

  return `${cut}…`;
}

function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' });
}

/** Greedy word wrap against the measured width of the current font. */
function wrap(ctx: CanvasRenderingContext2D, text: string, maxWidth: number, maxLines: number): string[] {
  const words = text.split(/\s+/).filter(Boolean);
  const lines: string[] = [];
  let current = '';
  let consumed = 0;

  for (const word of words) {
    const candidate = current ? `${current} ${word}` : word;
    if (ctx.measureText(candidate).width <= maxWidth) {
      current = candidate;
      consumed++;
      continue;
    }

    if (current) lines.push(current);
    if (lines.length === maxLines) {
      current = '';
      break;
    }

    current = word;
    consumed++;
  }

  if (current && lines.length < maxLines) lines.push(current);

  if (lines.length === maxLines && consumed < words.length) {
    lines[maxLines - 1] = `${lines[maxLines - 1].replace(/[.,;:]$/, '')}…`;
  }

  return lines;
}

export async function renderShareImage(story: StoryDetail, appUrl: string): Promise<Blob> {
  const accent = (CATEGORY_ACCENT[story.category] ?? CATEGORY_ACCENT.Unknown).dark;
  const label = CATEGORY_LABELS[story.category] ?? CATEGORY_LABELS.Unknown;
  const subject = story.visualEntity?.trim() || story.topics[0]?.name || label;

  const plan = document.createElement('canvas').getContext('2d')!;
  const ops: Op[] = [];
  let y = BAR_H + PLATE_H + 76;
  let truncated = false;

  // ── planning helpers ──────────────────────────────────────────────────────
  // Everything is planned into ops first and painted afterwards, which is what
  // lets the canvas be sized to the content. An earlier version measured and drew
  // in two separate passes of the same arithmetic; they drifted and clipped the
  // footer off every card.

  const text = (t: string, font: string, fill: string, x = PAD, align?: CanvasTextAlign) =>
    ops.push({ kind: 'text', text: t, font, fill, x, y, align });

  const para = (t: string, font: string, fill: string, x: number, lineHeight: number, maxLines: number) => {
    plan.font = font;
    for (const line of wrap(plan, t, W - PAD - x, maxLines)) {
      ops.push({ kind: 'text', text: line, font, fill, x, y });
      y += lineHeight;
    }
  };

  const heading = (t: string) => {
    text(t, `bold 24px ${MONO}`, accent);
    y += 46;
  };

  const rule = () => {
    y += 18;
    ops.push({ kind: 'rule', y });
    y += 54;
  };

  /** Starts a section only if it can fit whole; otherwise flags the truncation. */
  const room = (estimate: number) => {
    if (truncated) return false;
    if (y + estimate + 160 > MAX_H) {
      truncated = true;
      return false;
    }
    return true;
  };

  // ── header ────────────────────────────────────────────────────────────────
  const meta = [formatDate(story.publishedAt), `${story.readingMinutes} DK OKUMA`]
    .filter(Boolean)
    .join(' · ')
    .toLocaleUpperCase('tr-TR');

  text(`› ${meta}`, `bold 24px ${MONO}`, FAINT);
  y += 64;

  para(story.title, `600 58px ${SERIF}`, INK, PAD, 70, 6);

  if (story.dek?.trim()) {
    y += 30;
    para(story.dek.trim(), `34px ${SERIF}`, MUTED, PAD, 48, 3);
  }

  // ── summary ───────────────────────────────────────────────────────────────
  if (story.summary?.trim() && room(400)) {
    rule();
    heading('ÖZET');
    para(story.summary.trim(), `34px ${SERIF}`, MUTED, PAD, 50, 14);
  }

  // ── key points ────────────────────────────────────────────────────────────
  if (story.keyPoints.length && room(story.keyPoints.length * 110)) {
    rule();
    heading('ÖNE ÇIKANLAR');
    for (const point of story.keyPoints) {
      const bulletY = y;
      para(point, `30px ${SANS}`, MUTED, PAD + 36, 42, 4);
      ops.push({ kind: 'text', text: '•', font: `30px ${SANS}`, fill: accent, x: PAD, y: bulletY });
      y += 20;
    }
  }

  // ── the three questions ───────────────────────────────────────────────────
  const questions: [string, string | null | undefined][] = [
    ['NEDEN ÖNEMLİ?', story.whyItMatters],
    ['KİMLERİ ETKİLİYOR?', story.whoIsAffected],
    ['BEN NE YAPMALIYIM?', story.whatShouldIDo],
  ];

  const answered = questions.filter(([, body]) => body?.trim());
  if (answered.length && room(answered.length * 220)) {
    rule();
    for (const [question, body] of answered) {
      heading(question);
      para(body!.trim(), `30px ${SANS}`, MUTED, PAD, 44, 6);
      y += 34;
    }
    y -= 34;
  }

  // ── trust ─────────────────────────────────────────────────────────────────
  if (story.trust && room(420)) {
    rule();
    const band = trustBand(story.trust.total);
    heading(`GÜVEN · ${band.label.toLocaleUpperCase('tr-TR')}`);

    const rows: [string, number][] = [
      ['resmî kaynak', story.trust.officialSourceScore],
      ['doğrulama', story.trust.corroborationScore],
      ['güncellik', story.trust.recencyScore],
      ['teknik kesinlik', story.trust.technicalAccuracyScore],
    ];

    const barLeft = PAD + 280;
    const barMax = W - PAD - barLeft - 70;

    for (const [name, score] of rows) {
      text(name, `26px ${MONO}`, FAINT);
      ops.push({ kind: 'bar', x: barLeft, y: y - 18, w: barMax, fill: 'rgba(255,255,255,0.10)' });
      ops.push({
        kind: 'bar',
        x: barLeft,
        y: y - 18,
        w: Math.max(4, (barMax * Math.max(0, Math.min(100, score))) / 100),
        fill: score >= 60 ? TRUST : CAUTION,
      });
      text(String(score), `bold 26px ${MONO}`, INK, W - PAD, 'right');
      y += 48;
    }

    y += 12;
    text(`toplam ${story.trust.total}/100`, `bold 28px ${MONO}`, INK);
    y += 46;

    if (story.trust.explanation?.trim()) {
      para(story.trust.explanation.trim(), `26px ${SANS}`, FAINT, PAD, 38, 4);
    }
  }

  // ── AI commentary ─────────────────────────────────────────────────────────
  if (story.analysis && room(500)) {
    rule();
    heading('AI YORUMU');
    para(story.analysis.whyImportant, `30px ${SANS}`, MUTED, PAD, 44, 6);

    if (story.analysis.realImpact?.trim()) {
      y += 26;
      para(`Gerçek etkisi: ${story.analysis.realImpact.trim()}`, `30px ${SANS}`, MUTED, PAD, 44, 5);
    }

    y += 34;
    const verdicts = [
      HYPE_LABELS[story.analysis.hype],
      URGENCY_LABELS[story.analysis.learnUrgency],
      LONGEVITY_LABELS[story.analysis.longevity],
    ];
    for (const verdict of verdicts) {
      para(`· ${verdict}`, `26px ${MONO}`, FAINT, PAD, 40, 2);
    }
  }

  // ── how the outlets covered it ────────────────────────────────────────────
  if (story.comparison.length && room(story.comparison.length * 260)) {
    rule();
    heading('KAYNAKLAR NE DEDİ?');

    for (const point of story.comparison) {
      const kind =
        point.kind === 'Divergent' ? 'AYRIŞIYOR' : point.kind === 'Unique' ? 'TEK KAYNAKTA' : 'ORTAK';

      text(`[${kind}] ${point.sources.join(' · ')}`, `bold 24px ${MONO}`, point.kind === 'Divergent' ? CAUTION : FAINT);
      y += 40;
      para(point.text, `30px ${SANS}`, MUTED, PAD, 42, 3);
      y += 12;
      // The quote is why the comparison is checkable rather than asserted.
      para(`“${point.quote}”`, `italic 27px ${SERIF}`, FAINT, PAD + 24, 38, 4);
      text(`— ${point.quoteSource}`, `24px ${SANS}`, FAINT, PAD + 24);
      y += 56;
    }
  }

  // ── sources, with their own headlines ─────────────────────────────────────
  if (story.sources.length && room(story.sources.length * 130)) {
    rule();
    heading(`KAYNAKLAR (${story.sources.length})`);

    for (const source of story.sources) {
      text(
        `${source.isOfficial ? '✓ ' : '· '}${source.name}${source.isOfficial ? '  resmî' : ''}`,
        `bold 26px ${MONO}`,
        source.isOfficial ? TRUST : FAINT,
      );
      y += 40;
      para(source.articleTitle, `28px ${SERIF}`, MUTED, PAD + 24, 38, 2);
      y += 26;
    }
  }

  // ── footer ────────────────────────────────────────────────────────────────
  rule();
  if (truncated) {
    text('Devamı uygulamada — bu görsele sığmadı.', `24px ${SANS}`, CAUTION);
    y += 44;
  }
  const footerFont = `24px ${MONO}`;
  const brand = 'focus-ai';
  plan.font = footerFont;
  const urlSpace = W - PAD * 2 - plan.measureText(brand).width - 40;

  text(brand, footerFont, FAINT);
  text(
    fit(plan, `${appUrl}/story/${story.slug}`.replace(/^https?:\/\//, ''), footerFont, urlSpace),
    footerFont,
    FAINT,
    W - PAD,
    'right',
  );

  // ── paint ─────────────────────────────────────────────────────────────────
  const canvas = document.createElement('canvas');
  canvas.width = W;
  canvas.height = Math.round(y + PAD);

  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = PAPER;
  ctx.fillRect(0, 0, W, canvas.height);

  ctx.fillStyle = BAR;
  ctx.fillRect(0, 0, W, BAR_H);
  ctx.fillStyle = accent;
  ctx.beginPath();
  ctx.arc(PAD + 8, BAR_H / 2, 9, 0, Math.PI * 2);
  ctx.fill();

  ctx.font = `26px ${MONO}`;
  ctx.textBaseline = 'middle';
  ctx.fillStyle = '#8aa0bd';
  ctx.fillText(`focus:~/${CATEGORY_SLUG[story.category]}`, PAD + 34, BAR_H / 2 + 2);
  ctx.fillStyle = '#5f7089';
  ctx.textAlign = 'right';
  ctx.fillText(`${story.sources.length} kaynak`, W - PAD, BAR_H / 2 + 2);
  ctx.textAlign = 'left';
  ctx.textBaseline = 'alphabetic';

  ctx.fillStyle = accent;
  ctx.globalAlpha = 0.13;
  ctx.fillRect(0, BAR_H, W, PLATE_H);
  ctx.globalAlpha = 0.3;
  ctx.strokeStyle = accent;
  ctx.lineWidth = 2;
  ctx.strokeRect(PAD / 2, BAR_H + PAD / 2, W - PAD, PLATE_H - PAD);
  ctx.globalAlpha = 1;

  let size = subject.length <= 4 ? 160 : subject.length <= 8 ? 116 : subject.length <= 14 ? 86 : 60;
  ctx.font = `600 ${size}px ${SERIF}`;
  while (ctx.measureText(subject).width > W - PAD * 2 && size > 38) {
    size -= 6;
    ctx.font = `600 ${size}px ${SERIF}`;
  }
  ctx.fillStyle = INK;
  ctx.fillText(subject, PAD, BAR_H + PLATE_H - 100);
  ctx.font = `bold 26px ${MONO}`;
  ctx.fillStyle = accent;
  ctx.fillText(label.toLocaleUpperCase('tr-TR'), PAD, BAR_H + PLATE_H - 52);

  for (const op of ops) {
    if (op.kind === 'rule') {
      ctx.strokeStyle = RULE;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(PAD, op.y);
      ctx.lineTo(W - PAD, op.y);
      ctx.stroke();
      continue;
    }

    if (op.kind === 'bar') {
      ctx.fillStyle = op.fill;
      ctx.fillRect(op.x, op.y, op.w, 14);
      continue;
    }

    ctx.font = op.font;
    ctx.fillStyle = op.fill;
    ctx.textAlign = op.align ?? 'left';
    ctx.fillText(op.text, op.x, op.y);
  }
  ctx.textAlign = 'left';

  return new Promise((resolve, reject) => {
    canvas.toBlob(
      (blob) => (blob ? resolve(blob) : reject(new Error('Görsel oluşturulamadı.'))),
      'image/png',
    );
  });
}
