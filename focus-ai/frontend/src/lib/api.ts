import type {
  AskResult,
  AuthResult,
  Digest,
  DigestPeriod,
  InteractionType,
  LearningSuggestion,
  Paged,
  Profile,
  Source,
  SourceHealthReport,
  StoryCard,
  StoryDetail,
  Topic,
  Trend,
  User,
} from './types';

/**
 * Where the API lives.
 *
 * An explicitly empty NEXT_PUBLIC_API_URL means "same origin": every request
 * becomes a relative /api/... path. That is the mode to use behind a single
 * HTTPS hostname (the Tailscale setup in the README), where it also removes CORS
 * from the picture entirely. Safe because every call in this file runs in the
 * browser — the one server component, layout.tsx, does no fetching.
 *
 * Baked in at build time by Next.js, so changing it means rebuilding the image.
 */
export const API_BASE =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/$/, '') ?? 'http://localhost:5210';

const ACCESS_TOKEN_KEY = 'focusai.access';
const REFRESH_TOKEN_KEY = 'focusai.refresh';

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly fieldErrors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * Human-readable message for a caught error.
 *
 * A validation failure carries per-field messages in `fieldErrors`; showing only
 * the generic "Bir veya daha fazla doğrulama hatası oluştu." leaves the user
 * guessing which field was wrong. This surfaces the actual field messages.
 */
export function describeError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    if (error.fieldErrors && Object.keys(error.fieldErrors).length > 0) {
      return Object.values(error.fieldErrors).flat().join(' ');
    }
    return error.message;
  }
  return fallback;
}

export const tokenStore = {
  get access(): string | null {
    if (typeof window === 'undefined') return null;
    return window.localStorage.getItem(ACCESS_TOKEN_KEY);
  },
  get refresh(): string | null {
    if (typeof window === 'undefined') return null;
    return window.localStorage.getItem(REFRESH_TOKEN_KEY);
  },
  set(access: string, refresh: string) {
    if (typeof window === 'undefined') return;
    window.localStorage.setItem(ACCESS_TOKEN_KEY, access);
    window.localStorage.setItem(REFRESH_TOKEN_KEY, refresh);
  },
  clear() {
    if (typeof window === 'undefined') return;
    window.localStorage.removeItem(ACCESS_TOKEN_KEY);
    window.localStorage.removeItem(REFRESH_TOKEN_KEY);
  },
};

/**
 * Serialises concurrent refreshes. Without this, a page that fires five requests
 * on mount would race five refreshes and invalidate its own rotated token.
 */
let refreshInFlight: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = tokenStore.refresh;
  if (!refreshToken) return false;

  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(`${API_BASE}/api/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });

      if (!response.ok) {
        tokenStore.clear();
        return false;
      }

      const result = (await response.json()) as AuthResult;
      tokenStore.set(result.accessToken, result.refreshToken);
      return true;
    } catch {
      return false;
    } finally {
      refreshInFlight = null;
    }
  })();

  return refreshInFlight;
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Set false for endpoints that work fine unauthenticated. */
  auth?: boolean;
  signal?: AbortSignal;
  /** Server components pass a token explicitly since localStorage is unavailable. */
  token?: string;
  cache?: RequestCache;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, auth = true, signal, token, cache } = options;

  const send = async (accessToken: string | null): Promise<Response> => {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;

    return fetch(`${API_BASE}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
      cache: cache ?? 'no-store',
    });
  };

  const initialToken = token ?? (auth ? tokenStore.access : null);
  let response = await send(initialToken);

  // One transparent retry after a refresh; a second 401 is a real auth failure.
  if (response.status === 401 && auth && !token && tokenStore.refresh) {
    if (await refreshAccessToken()) {
      response = await send(tokenStore.access);
    }
  }

  if (response.status === 204) {
    return undefined as T;
  }

  if (!response.ok) {
    let message = `İstek başarısız (HTTP ${response.status})`;
    let fieldErrors: Record<string, string[]> | undefined;

    try {
      const problem = await response.json();
      if (typeof problem?.detail === 'string') message = problem.detail;
      else if (typeof problem?.title === 'string') message = problem.title;
      if (problem?.errors) fieldErrors = problem.errors;
    } catch {
      // Non-JSON error body — the status-based message above is the best we have.
    }

    throw new ApiError(message, response.status, fieldErrors);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const qs = (params: Record<string, string | number | boolean | undefined | null>): string => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }
  const query = search.toString();
  return query ? `?${query}` : '';
};

export const api = {
  auth: {
    register: (payload: {
      email: string;
      password: string;
      displayName: string;
      timeZone?: string;
      locale?: string;
      interestSlugs?: string[];
    }) => request<AuthResult>('/api/auth/register', { method: 'POST', body: payload, auth: false }),

    login: (email: string, password: string) =>
      request<AuthResult>('/api/auth/login', {
        method: 'POST',
        body: { email, password },
        auth: false,
      }),

    logout: (refreshToken: string) =>
      request<void>('/api/auth/logout', { method: 'POST', body: { refreshToken }, auth: false }),

    me: (token?: string) => request<User>('/api/auth/me', { token }),
  },

  stories: {
    feed: (params: {
      category?: string;
      topic?: string;
      page?: number;
      pageSize?: number;
      withinHours?: number;
      personalized?: boolean;
    } = {}) => request<Paged<StoryCard>>(`/api/stories${qs(params)}`),

    top: (withinHours = 24) => request<StoryCard | undefined>(`/api/stories/top${qs({ withinHours })}`),

    detail: (slug: string, token?: string) =>
      request<StoryDetail>(`/api/stories/${encodeURIComponent(slug)}`, { token }),

    recordInteraction: (storyId: string, type: InteractionType, dwellSeconds?: number, surface?: string) =>
      request<void>(`/api/stories/${storyId}/interactions`, {
        method: 'POST',
        body: { type, dwellSeconds, surface },
      }),

    /**
     * Retracts a "faydalı" / "az göster" verdict. Interactions are append-only and
     * the ranker reads the newest one, so getting back to "no opinion" needs a
     * delete rather than another POST.
     */
    clearFeedback: (storyId: string) =>
      request<void>(`/api/stories/${storyId}/interactions/feedback`, { method: 'DELETE' }),
  },

  digest: {
    get: (period: DigestPeriod = 'Daily', date?: string) =>
      request<Digest | undefined>(
        `/api/digest/${period === 'Weekly' ? 'weekly' : 'daily'}${qs({ date })}`,
      ),
  },

  search: (query: string, page = 1, pageSize = 20) =>
    request<Paged<StoryCard>>(`/api/search${qs({ q: query, page, pageSize })}`),

  ask: (question: string) =>
    request<AskResult>('/api/ask', { method: 'POST', body: { question } }),

  bookmarks: {
    list: (page = 1, pageSize = 20, tag?: string) =>
      request<Paged<{ id: string; note?: string | null; tags: string[]; createdAt: string; story: StoryCard }>>(
        `/api/bookmarks${qs({ page, pageSize, tag })}`,
      ),

    toggle: (storyId: string, note?: string, tags?: string[]) =>
      request<{ saved: boolean }>('/api/bookmarks/toggle', {
        method: 'POST',
        body: { storyId, note, tags },
      }),
  },

  profile: {
    get: () => request<Profile>('/api/profile'),

    update: (payload: Partial<{
      displayName: string;
      headline: string;
      experienceLevel: string;
      dailyDigestHour: number;
      dailyStoryCount: number;
      dailyLearningMinutes: number;
      timeZone: string;
      locale: string;
    }>) => request<void>('/api/profile', { method: 'PUT', body: payload }),

    setInterests: (topicSlugs: string[]) =>
      request<void>('/api/profile/interests', { method: 'PUT', body: { topicSlugs } }),

    toggleMute: (topicSlug: string) =>
      request<{ muted: boolean }>('/api/profile/mute', { method: 'POST', body: { topicSlug } }),

    toggleFavoriteSource: (sourceId: string) =>
      request<{ favorite: boolean }>('/api/profile/favorite-source', {
        method: 'POST',
        body: { sourceId },
      }),

    updateNotifications: (payload: Partial<{
      morningDigest: boolean;
      eveningDigest: boolean;
      bigNewsOnly: boolean;
      weeklyDigest: boolean;
      bigNewsThreshold: number;
      quietHoursStart: number;
      quietHoursEnd: number;
    }>) => request<void>('/api/profile/notifications', { method: 'PUT', body: payload }),
  },

  learning: {
    today: () => request<LearningSuggestion | undefined>('/api/learning/today'),
    history: (take = 30) => request<LearningSuggestion[]>(`/api/learning/history${qs({ take })}`),
    setStatus: (id: string, status: string) =>
      request<void>(`/api/learning/${id}/status`, { method: 'PUT', body: { status } }),
  },

  trends: (period: DigestPeriod = 'Monthly', take = 20) =>
    request<Trend[]>(`/api/trends${qs({ period, take })}`),

  topics: (all = false) => request<Topic[]>(`/api/topics${qs({ all })}`),

  sources: (category?: string) => request<Source[]>(`/api/sources${qs({ category })}`),

  /**
   * Whether each source is actually working. The endpoint is anonymous-safe, but
   * the token is still sent when there is one — that is what fills in isFavorite.
   */
  sourceHealth: () => request<SourceHealthReport>('/api/sources/health'),

  admin: {
    setSourceEnabled: (sourceId: string, enabled: boolean) =>
      request<{ enabled: boolean }>(`/api/admin/sources/${sourceId}/enabled`, {
        method: 'PUT',
        body: { enabled },
      }),
  },
};
