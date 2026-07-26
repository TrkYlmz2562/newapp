'use client';

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api, tokenStore } from '@/lib/api';
import type { User } from '@/lib/types';

interface AuthState {
  user: User | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (payload: {
    email: string;
    password: string;
    displayName: string;
    interestSlugs?: string[];
  }) => Promise<void>;
  logout: () => Promise<void>;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  const refreshUser = useCallback(async () => {
    if (!tokenStore.access) {
      setUser(null);
      setLoading(false);
      return;
    }

    try {
      setUser(await api.auth.me());
    } catch {
      // An expired session is a normal state, not an error worth surfacing —
      // the API client already tried to refresh before giving up.
      tokenStore.clear();
      setUser(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refreshUser();
  }, [refreshUser]);

  const login = useCallback(async (email: string, password: string) => {
    const result = await api.auth.login(email, password);
    tokenStore.set(result.accessToken, result.refreshToken);
    setUser(result.user);
  }, []);

  const register = useCallback(
    async (payload: { email: string; password: string; displayName: string; interestSlugs?: string[] }) => {
      const result = await api.auth.register({
        ...payload,
        // Sending the browser's zone is what makes the 08:00 digest land at the
        // reader's 08:00 rather than the server's.
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        locale: 'tr',
      });

      tokenStore.set(result.accessToken, result.refreshToken);
      setUser(result.user);
    },
    [],
  );

  const logout = useCallback(async () => {
    const refresh = tokenStore.refresh;
    tokenStore.clear();
    setUser(null);

    if (refresh) {
      // Best effort: the local session is already gone either way.
      try {
        await api.auth.logout(refresh);
      } catch {
        /* ignored */
      }
    }
  }, []);

  const value = useMemo<AuthState>(
    () => ({ user, loading, login, register, logout, refreshUser }),
    [user, loading, login, register, logout, refreshUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used inside <AuthProvider>.');
  }
  return context;
}
