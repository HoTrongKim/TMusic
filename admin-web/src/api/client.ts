const API_BASE = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/+$/, '');

let inMemoryCsrfToken: string | null = null;

export function setCsrfToken(token: string | null) {
  inMemoryCsrfToken = token;
}

function readCookie(name: string): string | null {
  if (typeof document === 'undefined') return null;
  const escaped = name.replace(/[-[\]/{}()*+?.\\^$|]/g, '\\$&');
  const match = document.cookie.match(new RegExp(`(?:^|; )${escaped}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : null;
}

export class ApiError extends Error {
  status: number;
  code: string;
  detail?: string;

  constructor(status: number, code: string, detail?: string) {
    super(detail || code);
    this.status = status;
    this.code = code;
    this.detail = detail;
  }
}

export interface AdminProfile {
  id: string;
  email: string;
  displayName: string;
  emailVerified?: boolean;
  mfaEnabled: boolean;
  roles: string[];
  permissions: string[];
  csrfToken?: string;
  createdAt?: string;
  lastActiveAt?: string | null;
}

async function rawRequest(path: string, init: RequestInit = {}): Promise<Response> {
  const method = (init.method || 'GET').toUpperCase();
  const headers = new Headers(init.headers);
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    const csrf = inMemoryCsrfToken || readCookie('tmusic_admin_csrf');
    if (csrf) headers.set('X-CSRF-Token', csrf);
  }
  return fetch(`${API_BASE}${path}`, {
    ...init,
    method,
    headers,
    credentials: 'include',
  });
}

let refreshPromise: Promise<boolean> | null = null;

async function tryRefreshSession(): Promise<boolean> {
  if (!refreshPromise) {
    refreshPromise = (async () => {
      try {
        const res = await rawRequest('/api/admin/auth/refresh', { method: 'POST' });
        if (!res.ok) return false;
        const data = await res.json();
        if (data?.csrfToken) setCsrfToken(data.csrfToken);
        return true;
      } catch {
        return false;
      } finally {
        refreshPromise = null;
      }
    })();
  }
  return refreshPromise;
}

export async function apiRequest<T>(path: string, init: RequestInit = {}, allowRefresh = true): Promise<T> {
  let response = await rawRequest(path, init);
  if (response.status === 401 && allowRefresh && !path.startsWith('/api/admin/auth/')) {
    const refreshed = await tryRefreshSession();
    if (refreshed) {
      response = await rawRequest(path, init);
    }
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  let parsed: unknown = null;
  if (text) {
    try {
      parsed = JSON.parse(text);
    } catch {
      parsed = { detail: text };
    }
  }

  if (!response.ok) {
    const errObj = (parsed && typeof parsed === 'object' ? parsed : {}) as Record<string, unknown>;
    throw new ApiError(
      response.status,
      String(errObj.code || errObj.title || `HTTP_${response.status}`),
      typeof errObj.detail === 'string' ? errObj.detail : undefined,
    );
  }

  return parsed as T;
}

export async function apiDownloadBlob(path: string, filename: string): Promise<void> {
  const response = await rawRequest(path, { method: 'GET' });
  if (!response.ok) {
    throw new ApiError(response.status, 'EXPORT_FAILED', 'Không thể xuất báo cáo CSV.');
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
