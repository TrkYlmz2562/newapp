import type { CommitmentTier, FinanceInstrument, FinanceItem } from './types';

/**
 * What each tier means, in the reader's words. Deliberately a phrase, never a
 * number: a score beside a financial claim reads as precision this product does
 * not have, and cannot have — certainty here is a property of the document, not
 * a measurement.
 */
export const TIER_LABELS: Record<CommitmentTier, string> = {
  Unknown: 'Sınıflandırılmadı',
  Realized: 'Gerçekleşti',
  EnactedDated: 'Yürürlükte · tarihi belli',
  OfficialCommitment: 'Resmî taahhüt',
  ConditionalPending: 'Şarta bağlı',
  StatedIntent: 'Niyet · hedef',
  UnverifiedClaim: 'Doğrulanmamış iddia',
  AnalystSpeculation: 'Tahmin · yorum',
};

/** One line explaining why the item sits at this tier. */
export const TIER_HELP: Record<CommitmentTier, string> = {
  Unknown: '',
  Realized: 'Olay gerçekleşti ve kayda geçti.',
  EnactedDated: 'Bağlayıcı bir belge ileri bir tarihi sabitliyor.',
  OfficialCommitment: 'Kararı veren merci, kendi kararını tarihiyle duyurdu.',
  ConditionalPending: 'Gerçek bir karar var, ama adı konmuş bir onayı bekliyor.',
  StatedIntent: 'Niyet beyanı — bağlayıcı bir belge yok.',
  UnverifiedClaim: 'İsimsiz kaynaklara dayanıyor.',
  AnalystSpeculation: 'Üçüncü tarafın beklentisi.',
};

export const INSTRUMENT_LABELS: Record<FinanceInstrument, string> = {
  None: '',
  ResmiGazete: 'Resmî Gazete',
  Kap: 'KAP bildirimi',
  KurumKarari: 'Kurum kararı',
  Mahkeme: 'Mahkeme kararı',
  Sozlesme: 'Sözleşme',
  ResmiTakvim: 'Resmî takvim',
};

/**
 * The lifecycle a committed development moves through. Shown as a stepper rather
 * than a percentage: each step is a real, checkable event, so it does the job a
 * probability pretends to do without inventing one.
 */
export const LIFECYCLE = ['Açıklandı', 'Onaylandı', 'Yürürlükte', 'Uygulandı'] as const;

export function lifecycleStep(tier: CommitmentTier): number {
  switch (tier) {
    case 'Realized':
      return 3;
    case 'EnactedDated':
      return 2;
    case 'OfficialCommitment':
      return 1;
    case 'ConditionalPending':
      return 0;
    default:
      return -1;
  }
}

/** "5 ay 6 gün" — a countdown to a documented date is a fact, not a forecast. */
export function timeUntil(isoDate: string | null | undefined): string | null {
  if (!isoDate) return null;

  const target = new Date(`${isoDate}T00:00:00Z`).getTime();
  if (Number.isNaN(target)) return null;

  const days = Math.round((target - Date.now()) / 86_400_000);
  if (days < 0) return null;
  if (days === 0) return 'bugün';
  if (days === 1) return 'yarın';
  if (days < 30) return `${days} gün kaldı`;

  const months = Math.floor(days / 30);
  const rest = days % 30;
  return rest === 0 ? `${months} ay kaldı` : `${months} ay ${rest} gün kaldı`;
}

export function formatEventDate(item: FinanceItem): string | null {
  if (!item.eventDate) return item.dateText ?? null;

  const date = new Date(`${item.eventDate}T00:00:00Z`);
  if (Number.isNaN(date.getTime())) return item.dateText ?? null;

  return date.toLocaleDateString('tr-TR', {
    day: item.datePrecision === 'Day' ? 'numeric' : undefined,
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  });
}
