import React, { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { AdminProfile, apiRequest, setCsrfToken } from '../api/client';

export interface ToastItem {
  id: number;
  message: string;
  tone: 'info' | 'success' | 'danger';
}

interface AuthContextValue {
  admin: AdminProfile | null;
  loading: boolean;
  theme: 'dark' | 'light';
  toggleTheme: () => void;
  refreshMe: () => Promise<void>;
  setAdminSession: (profile: AdminProfile, csrf?: string) => void;
  logout: () => Promise<void>;
  hasPermission: (perm: string) => boolean;
  toasts: ToastItem[];
  notify: (message: string, tone?: 'info' | 'success' | 'danger') => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [admin, setAdmin] = useState<AdminProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [theme, setTheme] = useState<'dark' | 'light'>(() => {
    try {
      const saved = window.localStorage.getItem('tmusic_admin_theme');
      return saved === 'light' ? 'light' : 'dark';
    } catch {
      return 'dark';
    }
  });

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    try {
      window.localStorage.setItem('tmusic_admin_theme', theme);
    } catch {
      // ignore storage quota errors
    }
  }, [theme]);

  const notify = useCallback((message: string, tone: 'info' | 'success' | 'danger' = 'info') => {
    const id = Date.now() + Math.floor(Math.random() * 1000);
    setToasts((prev) => [...prev, { id, message, tone }]);
    window.setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id));
    }, 4500);
  }, []);

  const refreshMe = useCallback(async () => {
    try {
      const profile = await apiRequest<AdminProfile>('/api/admin/auth/me', {}, true);
      if (profile.csrfToken) setCsrfToken(profile.csrfToken);
      setAdmin(profile);
    } catch {
      setAdmin(null);
      setCsrfToken(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refreshMe();
  }, [refreshMe]);

  const setAdminSession = useCallback((profile: AdminProfile, csrf?: string) => {
    if (csrf || profile.csrfToken) setCsrfToken(csrf || profile.csrfToken || null);
    setAdmin(profile);
    setLoading(false);
  }, []);

  const logout = useCallback(async () => {
    try {
      await apiRequest('/api/admin/auth/logout', { method: 'POST' }, false);
    } catch {
      // clear local state even if session already expired
    }
    setCsrfToken(null);
    setAdmin(null);
    notify('Đã đăng xuất khỏi TMusic Admin.', 'info');
  }, [notify]);

  const toggleTheme = useCallback(() => {
    setTheme((prev) => (prev === 'dark' ? 'light' : 'dark'));
  }, []);

  const hasPermission = useCallback(
    (perm: string) => {
      if (!admin) return false;
      if (admin.roles.includes('SuperAdmin')) return true;
      return admin.permissions.includes(perm);
    },
    [admin],
  );

  return (
    <AuthContext.Provider
      value={{
        admin,
        loading,
        theme,
        toggleTheme,
        refreshMe,
        setAdminSession,
        logout,
        hasPermission,
        toasts,
        notify,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
