import { CATEGORY_ACCENT, CATEGORY_LABELS, CATEGORY_SLUG } from './format';
import type { StoryDetail } from './types';

/**
 * Draws a story as a shareable card.
 *
 * Deliberately does NOT draw the story's hero photo: a remote image taints the
 * canvas under the browser's origin rules and toBlob() then throws, so the export
 * would fail exactly on the stories that have the nicest picture. The generated
 * plate is used instead — it is ours, it always works, and it matches the app.
 *
 * Always dark, whatever theme the reader is using: a share image is looked at
 * inside someone else's chat app, where matching our UI matters less than being
 * legible and consistent.
 */

const W = 1080;
const PAD = 64;
const BAR_H = 92;
const PLATE_H = 360;

const INK = '#eef2f8';
const MUTED = '#a9b4c6';
const FAINT = '#7e8da3';
const PAPER = '#0e141e';
const BAR = '#0c1017';
const RULE = 'rgba(255,255,255,0.12)';
const TRUST = '#34d39e';

const SERIF = 'Georgia, "Times New Roman", serif';
const MONO = 'ui-monospace, "SF Mono", Menlo, Consolas, monospace';
const SANS = 'system-ui, -apple-system, "Segoe UI", sans-serif';

/**
 * One piece of text with everything needed to draw it. The body is planned into
 * these first and painted afterwards, so the canvas can be sized to the content.
 *
 * Measuring and drawing used to be two passes of the same arithmetic, which
 * silently drifted apart and clipped the footer off the bottom of every card.
 * Planning once removes the possibility.
 */
interface Op {
  text: string;
  font: string;
  fill: string;
  x: number;
  y: number;
  align?: CanvasTextAlign;
}

const SANS_BULLET = `30px ${SANS}`;

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

  // Mark the cut so a clipped paragraph reads as deliberate rather than broken.
  if (lines.length === maxLines && consumed < words.length) {
    lines[maxLines - 1] = `${lines[maxLines - 1].replace(/[.,;:]$/, '')}…`;
  }

  return lines;
}

function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' });
}

/** Renders the card and hands back a PNG blob. */
export async function renderShareImage(story: StoryDetail, appUrl: string): Promise<Blob> {
  const accent = (CATEGORY_ACCENT[story.category] ?? CATEGORY_ACCENT.Unknown).dark;
  const label = CATEGORY_LABELS[story.category] ?? CATEGORY_LABELS.Unknown;
  const subject = story.visualEntity?.trim() || story.topics[0]?.name || label;
  const summary = story.summary?.trim() || story.dek?.trim() || '';
  const points = story.keyPoints.slice(0, 3);
  const inner = W - PAD * 2;

  const plan = document.createElement('canvas').getContext('2d')!;
  const ops: Op[] = [];
  let y = BAR_H + PLATE_H + 72;

  const push = (text: string, font: string, fill: string, x = PAD, align?: CanvasTextAlign) =>
    ops.push({ text, font, fill, x, y, align });

  const block = (text: string, font: string, fill: string, x: number, lineHeight: number, maxLines: number) => {
    plan.font = font;
    for (const line of wrap(plan, text, W - PAD - x, maxLines)) {
      ops.push({ text: line, font, fill, x, y });
      y += lineHeight;
    }
  };

  push(`› ${formatDate(story.publishedAt).toLocaleUpperCase('tr-TR')}`, `bold 24px ${MONO}`, FAINT);
  y += 64;

  block(story.title, `600 58px ${SERIF}`, INK, PAD, 70, 4);

  if (summary) {
    y += 34;
    block(summary, `34px ${SERIF}`, MUTED, PAD, 48, 4);
  }

  if (points.length) {
    y += 40;
    for (const point of points) {
      const bulletY = y;
      block(point, SANS_BULLET, MUTED, PAD + 34, 42, 2);
      ops.push({ text: '•', font: SANS_BULLET, fill: accent, x: PAD, y: bulletY });
      y += 18;
    }
  }

  y += 30;
  const ruleY = y;
  y += 56;

  // Trust as the same block meter the card uses — never a bare number on its own.
  const trust = story.trust?.total ?? 0;
  const filled = Math.max(0, Math.min(10, Math.round(trust / 10)));
  const meterFont = `26px ${MONO}`;
  plan.font = meterFont;
  const labelW = plan.measureText('güven ').width;
  const filledW = plan.measureText('▓'.repeat(filled)).width;
  const fullW = plan.measureText('▓'.repeat(10)).width;

  push('güven', meterFont, FAINT);
  push('▓'.repeat(filled), meterFont, TRUST, PAD + labelW);
  push('░'.repeat(10 - filled), meterFont, 'rgba(255,255,255,0.18)', PAD + labelW + filledW);
  push(String(trust), `bold 26px ${MONO}`, INK, PAD + labelW + fullW + 16);

  y += 62;
  push('focus-ai', `24px ${MONO}`, FAINT);
  push(`${appUrl}/story/${story.slug}`.replace(/^https?:\/\//, ''), `24px ${MONO}`, FAINT, W - PAD, 'right');

  const canvas = document.createElement('canvas');
  canvas.width = W;
  // Descenders on the footer line sit below its baseline, so the bottom margin is
  // a full pad rather than the ~12px the glyphs strictly need.
  canvas.height = Math.round(y + PAD);

  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = PAPER;
  ctx.fillRect(0, 0, W, canvas.height);

  // ── terminal bar ──────────────────────────────────────────────────────────
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

  // ── plate ─────────────────────────────────────────────────────────────────
  ctx.fillStyle = accent;
  ctx.globalAlpha = 0.13;
  ctx.fillRect(0, BAR_H, W, PLATE_H);
  ctx.globalAlpha = 0.3;
  ctx.strokeStyle = accent;
  ctx.lineWidth = 2;
  ctx.strokeRect(PAD / 2, BAR_H + PAD / 2, W - PAD, PLATE_H - PAD);
  ctx.globalAlpha = 1;

  // Autofit: a two-character designation becomes a poster, a long one steps down.
  let size = subject.length <= 4 ? 170 : subject.length <= 8 ? 120 : subject.length <= 14 ? 88 : 62;
  ctx.font = `600 ${size}px ${SERIF}`;
  while (ctx.measureText(subject).width > inner && size > 38) {
    size -= 6;
    ctx.font = `600 ${size}px ${SERIF}`;
  }

  ctx.fillStyle = INK;
  ctx.fillText(subject, PAD, BAR_H + PLATE_H - 104);
  ctx.font = `bold 26px ${MONO}`;
  ctx.fillStyle = accent;
  ctx.fillText(label.toLocaleUpperCase('tr-TR'), PAD, BAR_H + PLATE_H - 54);

  // ── body ──────────────────────────────────────────────────────────────────
  ctx.strokeStyle = RULE;
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(PAD, ruleY);
  ctx.lineTo(W - PAD, ruleY);
  ctx.stroke();

  for (const op of ops) {
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
