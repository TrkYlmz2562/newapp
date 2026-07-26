/**
 * Mirrors the DTOs in FocusAI.Application/Dtos. Enums arrive as names because
 * the API registers JsonStringEnumConverter.
 */

export type ContentCategory =
  | 'Unknown'
  | 'Ai'
  | 'Software'
  | 'OpenSource'
  | 'Startup'
  | 'Science'
  | 'Career'
  | 'Tools'
  | 'Security'
  | 'Hardware'
  | 'Product'
  | 'Finance';

export type HypeLevel = 'Understated' | 'Accurate' | 'SlightlyOverhyped' | 'Overhyped';
export type LearnUrgency = 'Now' | 'ThisQuarter' | 'Watch' | 'Skip';
export type LongevityOutlook = 'Foundational' | 'Durable' | 'Uncertain' | 'Fading';
export type TopicKind =
  | 'Concept'
  | 'Language'
  | 'Framework'
  | 'Library'
  | 'Company'
  | 'Product'
  | 'Platform'
  | 'Role';
export type ExperienceLevel = 'Student' | 'Junior' | 'Mid' | 'Senior' | 'Staff' | 'Lead';
export type SubscriptionPlan = 'Free' | 'Premium' | 'Team';
export type LearningStatus = 'Suggested' | 'Started' | 'Completed' | 'Skipped';
export type DigestPeriod = 'Daily' | 'Weekly' | 'Monthly';
export type SourceCategory = 'OfficialBlog' | 'Community' | 'Code' | 'Academic' | 'Video' | 'News';
export type StoryLinkKind = 'Article' | 'Video' | 'GitHub' | 'Paper' | 'Discussion' | 'Documentation';

export type InteractionType =
  | 'Impression'
  | 'Open'
  | 'ReadComplete'
  | 'Save'
  | 'Unsave'
  | 'Helpful'
  | 'NotHelpful'
  | 'Dismiss'
  | 'Share'
  | 'SourceClick';

export interface Topic {
  id: string;
  name: string;
  slug: string;
  kind: TopicKind;
}

export interface StoryCard {
  id: string;
  slug: string;
  title: string;
  dek?: string | null;
  category: ContentCategory;
  summary?: string | null;
  whyItMatters?: string | null;
  heroImageUrl?: string | null;
  /** Subject set in display type when there is no photo — "M5", "OpenSSH", "20M$". */
  visualEntity?: string | null;
  publishedAt: string;
  trustScore: number;
  importanceScore: number;
  sourceCount: number;
  readingMinutes: number;
  topics: Topic[];
  isBookmarked: boolean;
  /** This reader already opened it — the reason the ranker pushed it down. */
  isRead: boolean;
  reason?: string | null;
}

export interface SourceRef {
  id: string;
  name: string;
  slug: string;
  url: string;
  isOfficial: boolean;
  iconUrl?: string | null;
  publishedAt: string;
  articleTitle: string;
}

export interface Trust {
  total: number;
  officialSourceScore: number;
  corroborationScore: number;
  recencyScore: number;
  technicalAccuracyScore: number;
  communityScore: number;
  explanation?: string | null;
}

export interface Analysis {
  whyImportant: string;
  realImpact?: string | null;
  hype: HypeLevel;
  hypeReasoning?: string | null;
  learnUrgency: LearnUrgency;
  longevity: LongevityOutlook;
  longevityReasoning?: string | null;
  stackNotes: Record<string, string>;
  confidence: number;
}

export type ComparisonKind = 'Shared' | 'Divergent' | 'Unique';

export interface ComparisonPoint {
  text: string;
  kind: ComparisonKind;
  /** Outlets this observation applies to, by name. */
  sources: string[];
  /** Verbatim sentence from `quoteSource` — the reader's way to check the claim. */
  quote: string;
  quoteSource: string;
}

export interface StoryLink {
  kind: StoryLinkKind;
  url: string;
  title: string;
  description?: string | null;
  thumbnailUrl?: string | null;
}

export interface StoryDetail {
  id: string;
  slug: string;
  title: string;
  dek?: string | null;
  category: ContentCategory;
  publishedAt: string;
  lastActivityAt: string;
  heroImageUrl?: string | null;
  visualEntity?: string | null;
  readingMinutes: number;
  importanceScore: number;
  summary?: string | null;
  whyItMatters?: string | null;
  whoIsAffected?: string | null;
  whatShouldIDo?: string | null;
  keyPoints: string[];
  extendedSummary?: string | null;
  trust?: Trust | null;
  analysis?: Analysis | null;
  sources: SourceRef[];
  links: StoryLink[];
  comparison: ComparisonPoint[];
  topics: Topic[];
  related: StoryCard[];
  isBookmarked: boolean;
  personalNote?: string | null;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
}

export interface DigestEntry {
  rank: number;
  reason?: string | null;
  story: StoryCard;
}

export interface Digest {
  id: string;
  date: string;
  period: DigestPeriod;
  intro?: string | null;
  readingMinutes: number;
  generatedAt: string;
  items: DigestEntry[];
}

export interface User {
  id: string;
  email: string;
  displayName: string;
  plan: SubscriptionPlan;
  timeZone: string;
  locale: string;
  roles: string[];
  onboardingCompleted: boolean;
}

export interface AuthResult {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: User;
}

export interface Interest {
  topicId: string;
  name: string;
  slug: string;
  weight: number;
  isExplicit: boolean;
}

export interface FavoriteSource {
  sourceId: string;
  name: string;
  slug: string;
  iconUrl?: string | null;
}

export interface NotificationSettings {
  morningDigest: boolean;
  eveningDigest: boolean;
  bigNewsOnly: boolean;
  weeklyDigest: boolean;
  bigNewsThreshold: number;
  quietHoursStart: number;
  quietHoursEnd: number;
}

export interface Badge {
  slug: string;
  name: string;
  description: string;
  emoji?: string | null;
  awardedAt: string;
}

export interface Streak {
  currentStreak: number;
  longestStreak: number;
  weeklyGoalDays: number;
  completedLearnings: number;
  totalStoriesRead: number;
  lastActiveDate?: string | null;
  badges: Badge[];
}

export interface Profile {
  userId: string;
  displayName: string;
  email: string;
  headline?: string | null;
  experienceLevel: ExperienceLevel;
  dailyDigestHour: number;
  dailyStoryCount: number;
  dailyLearningMinutes: number;
  onboardingCompleted: boolean;
  plan: SubscriptionPlan;
  interests: Interest[];
  mutedTopics: Topic[];
  favoriteSources: FavoriteSource[];
  notifications: NotificationSettings;
  streak: Streak;
}

export interface LearningResource {
  title: string;
  url: string;
  kind: string;
  estimatedMinutes: number;
}

export interface LearningSuggestion {
  id: string;
  date: string;
  title: string;
  rationale: string;
  estimatedMinutes: number;
  status: LearningStatus;
  topic?: Topic | null;
  resources: LearningResource[];
}

export interface TrendPoint {
  periodStart: string;
  storyCount: number;
  weightedImportance: number;
}

export interface Trend {
  topicId: string;
  name: string;
  slug: string;
  storyCount: number;
  sourceCount: number;
  weightedImportance: number;
  momentumPercent: number;
  history: TrendPoint[];
}

export interface Source {
  id: string;
  name: string;
  slug: string;
  websiteUrl: string;
  kind: string;
  category: SourceCategory;
  isOfficial: boolean;
  isEnabled: boolean;
  iconUrl?: string | null;
  lastSucceededAt?: string | null;
  consecutiveFailures: number;
}

export interface AskResult {
  answer: string;
  confidence: number;
  citations: StoryCard[];
}

export type CommitmentTier =
  | 'Unknown'
  | 'Realized'
  | 'EnactedDated'
  | 'OfficialCommitment'
  | 'ConditionalPending'
  | 'StatedIntent'
  | 'UnverifiedClaim'
  | 'AnalystSpeculation';

export type EventHorizon =
  | 'Unknown'
  | 'Completed'
  | 'Imminent'
  | 'Near'
  | 'Mid'
  | 'Long'
  | 'Undated';

export type FinanceInstrument =
  | 'None'
  | 'ResmiGazete'
  | 'Kap'
  | 'KurumKarari'
  | 'Mahkeme'
  | 'Sozlesme'
  | 'ResmiTakvim';

export type DatePrecision = 'None' | 'Day' | 'Window';

export interface FinanceItem {
  id: string;
  slug: string;
  title: string;
  tier: CommitmentTier;
  horizon: EventHorizon;
  instrument: FinanceInstrument;
  event?: string | null;
  dateText?: string | null;
  eventDate?: string | null;
  datePrecision: DatePrecision;
  /** The verbatim sentence that establishes the tier. Never paraphrased. */
  quote?: string | null;
  condition?: string | null;
  reference?: string | null;
  sourceCount: number;
  publishedAt: string;
  topics: string[];
}

export interface FinanceFeed {
  realized: FinanceItem[];
  soon: FinanceItem[];
  later: FinanceItem[];
  generatedAt: string;
}
