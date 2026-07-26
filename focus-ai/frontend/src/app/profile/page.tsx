'use client';

import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/components/AuthProvider';
import { ErrorState, PageHeader, SignInPrompt } from '@/components/Shell';
import { ThemeToggle } from '@/components/ThemeToggle';
import { api } from '@/lib/api';
import type { Profile, Topic } from '@/lib/types';

export default function ProfilePage() {
  const { user, loading: authLoading, logout } = useAuth();
  const router = useRouter();

  const [profile, setProfile] = useState<Profile | null>(null);
  const [topics, setTopics] = useState<Topic[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!user) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const [profileResult, topicsResult] = await Promise.all([api.profile.get(), api.topics()]);
      setProfile(profileResult);
      setTopics(topicsResult);
    } catch {
      setError('Profil yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }, [user]);

  useEffect(() => {
    if (!authLoading) void load();
  }, [authLoading, load]);

  const toggleInterest = async (slug: string) => {
    if (!profile || saving) return;

    const selected = new Set(profile.interests.map((interest) => interest.slug));
    if (selected.has(slug)) selected.delete(slug);
    else selected.add(slug);

    setSaving(true);
    try {
      await api.profile.setInterests([...selected]);
      await load();
    } catch {
      setError('İlgi alanları kaydedilemedi.');
    } finally {
      setSaving(false);
    }
  };

  const setAllInterests = async (all: boolean) => {
    if (saving) return;

    setSaving(true);
    try {
      await api.profile.setInterests(all ? topics.map((t) => t.slug) : []);
      await load();
    } catch {
      setError('İlgi alanları kaydedilemedi.');
    } finally {
      setSaving(false);
    }
  };

  const updateNotification = async (key: string, value: boolean) => {
    if (!profile) return;

    setProfile({ ...profile, notifications: { ...profile.notifications, [key]: value } });

    try {
      await api.profile.updateNotifications({ [key]: value });
    } catch {
      await load();
    }
  };

  if (!authLoading && !user) {
    return (
      <div className="space-y-4">
        <PageHeader title="Profil" />
        <SignInPrompt message="Profil, ilgi alanları ve bildirim ayarları için giriş yapmalısın." />
      </div>
    );
  }

  if (loading || authLoading) {
    return (
      <div className="space-y-4 px-4 pt-20 sm:px-5">
        <div className="skeleton h-24 w-full" />
        <div className="skeleton h-40 w-full" />
      </div>
    );
  }

  if (error && !profile) {
    return (
      <div className="space-y-4">
        <PageHeader title="Profil" />
        <ErrorState message={error} onRetry={load} />
      </div>
    );
  }

  if (!profile) return null;

  const selectedSlugs = new Set(profile.interests.map((interest) => interest.slug));

  return (
    <div className="space-y-4">
      <PageHeader title="Profil" subtitle={profile.email} />

      <div className="space-y-4 px-4 sm:px-5">
        <section className="card p-5">
          <div className="flex items-center gap-4">
            <span
              aria-hidden="true"
              className="grid h-14 w-14 place-items-center bg-focus-100 text-xl font-bold text-focus-700
                         dark:bg-focus-900/50 dark:text-focus-200"
            >
              {profile.displayName.charAt(0).toUpperCase()}
            </span>
            <div className="min-w-0">
              <p className="truncate font-semibold text-ink-900 dark:text-ink-50">{profile.displayName}</p>
              <p className="text-sm text-ink-500 dark:text-ink-400">
                {profile.headline ?? 'Teknoloji takipçisi'} · {profile.plan}
              </p>
            </div>
          </div>
        </section>

        <section className="card p-5">
          <h2 className="mb-1 text-sm font-semibold text-ink-800 dark:text-ink-100">Görünüm</h2>
          <p className="mb-3 text-xs text-ink-500 dark:text-ink-400">
            Varsayılan olarak telefonunun ayarını izler.
          </p>
          <ThemeToggle />
        </section>

        <section className="card p-5">
          <h2 className="mb-1 text-sm font-semibold text-ink-800 dark:text-ink-100">Kaynaklar</h2>
          <p className="mb-3 text-xs leading-relaxed text-ink-500 dark:text-ink-400">
            Hangi kaynakların gerçekten çalıştığını gör, takip etmek istediklerini seç.
            Bir akış hata vermeden aylardır içerik üretmiyor olabilir.
          </p>
          <Link href="/kaynaklar" className="btn-ghost">
            Kaynakları incele
          </Link>
        </section>

        {/* PRD section 15: kept deliberately plain — progress, not pressure. */}
        <section className="card p-5">
          <h2 className="mb-3 text-sm font-semibold text-ink-800 dark:text-ink-100">İlerleme</h2>
          <dl className="grid grid-cols-3 gap-3 text-center">
            <Stat label="Günlük seri" value={profile.streak.currentStreak} />
            <Stat label="Okunan haber" value={profile.streak.totalStoriesRead} />
            <Stat label="Tamamlanan" value={profile.streak.completedLearnings} />
          </dl>
          {profile.streak.badges.length > 0 && (
            <ul className="mt-4 flex flex-wrap gap-2">
              {profile.streak.badges.map((badge) => (
                <li
                  key={badge.slug}
                  title={badge.description}
                  className="chip bg-ink-100 text-ink-700 dark:bg-ink-800 dark:text-ink-200"
                >
                  {badge.emoji} {badge.name}
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="card p-5">
          <h2 className="mb-1 text-sm font-semibold text-ink-800 dark:text-ink-100">İlgi Alanları</h2>
          <p className="mb-3 text-xs text-ink-500 dark:text-ink-400">
            Seçtiklerin haberlerin sıralamasını doğrudan etkiler.
          </p>
          <div className="mb-3 flex items-center gap-3">
            <button
              type="button"
              onClick={() => setAllInterests(selectedSlugs.size !== topics.length)}
              disabled={saving}
              className="text-sm font-medium text-focus-600 disabled:opacity-60 dark:text-focus-400"
            >
              {selectedSlugs.size === topics.length && topics.length > 0
                ? 'Tümünü kaldır'
                : 'Tümünü seç'}
            </button>
            <span className="text-xs text-ink-500 dark:text-ink-400">
              {selectedSlugs.size}/{topics.length} seçili
            </span>
          </div>
          <div className="flex flex-wrap gap-2">
            {topics.map((topic) => {
              const active = selectedSlugs.has(topic.slug);
              return (
                <button
                  key={topic.id}
                  type="button"
                  onClick={() => toggleInterest(topic.slug)}
                  disabled={saving}
                  aria-pressed={active}
                  className={`chip border px-3 py-1.5 text-sm transition ${
                    active
                      ? 'border-focus-500 bg-focus-50 text-focus-700 dark:bg-focus-900/40 dark:text-focus-200'
                      : 'border-ink-300 bg-transparent text-ink-600 dark:border-ink-700 dark:bg-ink-900 dark:text-ink-300'
                  }`}
                >
                  {topic.name}
                </button>
              );
            })}
          </div>
        </section>

        <section className="card p-5">
          <h2 className="mb-3 text-sm font-semibold text-ink-800 dark:text-ink-100">Bildirimler</h2>
          <div className="space-y-1">
            <Toggle
              label="Sabah özeti"
              description={`Her gün saat ${profile.dailyDigestHour}:00`}
              checked={profile.notifications.morningDigest}
              onChange={(value) => updateNotification('morningDigest', value)}
            />
            <Toggle
              label="Akşam özeti"
              checked={profile.notifications.eveningDigest}
              onChange={(value) => updateNotification('eveningDigest', value)}
            />
            <Toggle
              label="Sadece büyük haberler"
              description={`Önem skoru ${profile.notifications.bigNewsThreshold} üstü`}
              checked={profile.notifications.bigNewsOnly}
              onChange={(value) => updateNotification('bigNewsOnly', value)}
            />
            <Toggle
              label="Haftalık özet"
              checked={profile.notifications.weeklyDigest}
              onChange={(value) => updateNotification('weeklyDigest', value)}
            />
          </div>
          <p className="mt-3 text-xs text-ink-400">
            Sessiz saatler: {profile.notifications.quietHoursStart}:00 –{' '}
            {profile.notifications.quietHoursEnd}:00
          </p>
        </section>

        {profile.mutedTopics.length > 0 && (
          <section className="card p-5">
            <h2 className="mb-3 text-sm font-semibold text-ink-800 dark:text-ink-100">
              Sessize alınan konular
            </h2>
            <div className="flex flex-wrap gap-2">
              {profile.mutedTopics.map((topic) => (
                <button
                  key={topic.id}
                  type="button"
                  onClick={async () => {
                    await api.profile.toggleMute(topic.slug);
                    await load();
                  }}
                  className="chip border border-ink-300 bg-transparent px-3 py-1.5 text-sm text-ink-600
                             dark:border-ink-700 dark:bg-ink-900 dark:text-ink-300"
                >
                  {topic.name} ✕
                </button>
              ))}
            </div>
          </section>
        )}

        <button
          type="button"
          onClick={async () => {
            await logout();
            router.push('/');
          }}
          className="btn-ghost w-full"
        >
          Çıkış yap
        </button>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="bg-ink-50 p-3 dark:bg-ink-800/60">
      <dt className="text-[11px] text-ink-500 dark:text-ink-400">{label}</dt>
      <dd className="text-xl font-bold tabular-nums text-ink-900 dark:text-ink-50">{value}</dd>
    </div>
  );
}

function Toggle({
  label,
  description,
  checked,
  onChange,
}: {
  label: string;
  description?: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-center justify-between gap-3 py-2">
      <span>
        <span className="block text-sm text-ink-800 dark:text-ink-100">{label}</span>
        {description && (
          <span className="block text-xs text-ink-500 dark:text-ink-400">{description}</span>
        )}
      </span>
      <span className="relative inline-flex shrink-0">
        <input
          type="checkbox"
          checked={checked}
          onChange={(event) => onChange(event.target.checked)}
          className="peer sr-only"
        />
        <span className="h-6 w-11 rounded-full bg-ink-200 transition peer-checked:bg-focus-600 dark:bg-ink-700" />
        <span className="absolute left-0.5 top-0.5 h-5 w-5 rounded-full bg-white transition peer-checked:translate-x-5" />
      </span>
    </label>
  );
}
