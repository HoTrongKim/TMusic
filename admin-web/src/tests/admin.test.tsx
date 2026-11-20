import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import App from '../App';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('TMusic Admin Web Dashboard', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.localStorage.clear();
    document.documentElement.setAttribute('data-theme', 'dark');
  });

  it('shows standard admin login screen when unauthenticated and signs in directly with email and password', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith('/api/admin/auth/me')) return jsonResponse({ code: 'UNAUTHORIZED' }, 401);
      if (url.endsWith('/api/admin/auth/refresh')) return jsonResponse({ code: 'SESSION_EXPIRED' }, 401);
      if (url.endsWith('/api/admin/auth/login')) {
        return jsonResponse({
          status: 'AUTHENTICATED',
          csrfToken: 'csrf-super',
          admin: {
            id: 'u-super',
            email: 'super@tmusic.test',
            displayName: 'SuperAdmin',
            mfaEnabled: false,
            roles: ['SuperAdmin'],
            permissions: ['analytics.read', 'users.read', 'settings.read', 'admins.manage'],
          },
        });
      }
      if (url.includes('/api/admin/dashboard')) {
        return jsonResponse({
          range: '30d',
          from: '2026-10-20T00:00:00Z',
          to: '2026-11-20T00:00:00Z',
          generatedAt: '2026-11-20T00:00:00Z',
          alerts: { openModerationFlags: 0, recentErrors24h: 0 },
          kpis: {
            totalAccounts: { value: 10, source: 'AspNetUsers' },
            newUsersToday: { value: 1, source: 'Today' },
            activeUsers7d: { value: 5, source: '7d' },
            activeUsers30d: { value: 8, source: '30d' },
            totalAlbums: { value: 4, source: 'Albums' },
            totalSavedSongs: { value: 20, source: 'UserLibrarySongs' },
            serverStoredMp3Count: { value: 2, source: 'Server MP3' },
            totalDownloadJobs: { value: 3, source: 'AudioDownloadJobs' },
            completedDownloads: { value: 2, source: 'COMPLETE' },
            failedDownloads: { value: 1, source: 'FAILED' },
            activeDownloads: { value: 0, source: 'ACTIVE' },
            serverStorageBytes: { value: 1024, audioBytes: 1024, coverBytes: 0, audioFileCount: 2, coverFileCount: 0, source: 'Disk' },
          },
          charts: {
            registrationsByDay: [],
            downloadsByDay: [],
            downloadStatusRatio: [],
            sourceDistribution: [],
            activeUsersByDay: [],
            storageHistory: { available: false, reason: 'Chưa có dữ liệu lịch sử.' },
          },
        });
      }
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);

    render(<App />);
    expect(await screen.findByText('Cổng quản trị hệ thống TMusic')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Email quản trị'), { target: { value: 'super@tmusic.test' } });
    fireEvent.change(screen.getByLabelText('Mật khẩu'), { target: { value: 'Strong-Pass!2026' } });
    fireEvent.click(screen.getByRole('button', { name: /Đăng nhập/i }));

    expect(await screen.findByText('Tổng quan vận hành TMusic')).toBeInTheDocument();
  });

  it('hides SuperAdmin & Settings sections for Moderator based on server permissions and renders real dashboard data', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith('/api/admin/auth/me')) {
        return jsonResponse({
          id: 'u-mod',
          email: 'mod@tmusic.test',
          displayName: 'Kiểm duyệt viên',
          mfaEnabled: true,
          roles: ['Moderator'],
          permissions: ['users.read', 'content.read', 'content.moderate', 'albums.read', 'downloads.read', 'analytics.read'],
          csrfToken: 'csrf-mod',
        });
      }
      if (url.includes('/api/admin/dashboard')) {
        return jsonResponse({
          range: '30d',
          from: '2026-10-20T00:00:00Z',
          to: '2026-11-20T00:00:00Z',
          generatedAt: '2026-11-20T00:00:00Z',
          alerts: { openModerationFlags: 1, recentErrors24h: 0 },
          kpis: {
            totalAccounts: { value: 42, source: 'AspNetUsers' },
            newUsersToday: { value: 3, source: 'Today' },
            activeUsers7d: { value: 12, source: '7d' },
            activeUsers30d: { value: 27, source: '30d' },
            totalAlbums: { value: 18, source: 'Albums' },
            totalSavedSongs: { value: 94, source: 'UserLibrarySongs' },
            serverStoredMp3Count: { value: 11, source: 'Server MP3' },
            totalDownloadJobs: { value: 15, source: 'AudioDownloadJobs' },
            completedDownloads: { value: 11, source: 'COMPLETE' },
            failedDownloads: { value: 4, source: 'FAILED' },
            activeDownloads: { value: 0, source: 'ACTIVE' },
            serverStorageBytes: { value: 10485760, audioBytes: 10000000, coverBytes: 485760, audioFileCount: 11, coverFileCount: 5, source: 'Disk' },
          },
          charts: {
            registrationsByDay: [{ date: '2026-11-19', count: 3 }],
            downloadsByDay: [{ date: '2026-11-19', total: 4, succeeded: 3, failed: 1, canceled: 0 }],
            downloadStatusRatio: [{ status: 'COMPLETE', count: 11 }],
            sourceDistribution: [{ provider: 'youtube', count: 9 }],
            activeUsersByDay: [{ date: '2026-11-19', count: 8 }],
            storageHistory: { available: false, reason: 'Chưa có dữ liệu lịch sử.' },
          },
        });
      }
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);

    render(<App />);
    expect(await screen.findByText('Tổng quan vận hành TMusic')).toBeInTheDocument();
    expect(await screen.findByText('42')).toBeInTheDocument();
    expect(screen.getByText('Chưa có dữ liệu lịch sử.')).toBeInTheDocument();

    // Moderator should NOT see Settings or Admin Management in navigation
    expect(screen.queryByRole('button', { name: 'Cấu hình' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Quản trị viên' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bài hát & Nội dung' })).toBeInTheDocument();

    // Dark Mode is default and toggles cleanly to Light Mode
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    fireEvent.click(screen.getByLabelText('Chuyển sang giao diện sáng'));
    await waitFor(() => expect(document.documentElement.getAttribute('data-theme')).toBe('light'));
  });
});
