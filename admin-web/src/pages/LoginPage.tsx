import React, { useState } from 'react';
import { Lock } from 'lucide-react';
import { AdminProfile, ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';

interface LoginResponse {
  csrfToken: string;
  admin: AdminProfile;
}

export function LoginPage() {
  const { setAdminSession, notify } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const res = await apiRequest<LoginResponse>(
        '/api/admin/auth/login',
        {
          method: 'POST',
          body: JSON.stringify({ email: email.trim(), password, deviceName: 'TMusic Admin Web' }),
        },
        false,
      );
      setPassword('');
      setAdminSession(res.admin, res.csrfToken);
      notify('Đăng nhập TMusic Admin thành công.', 'success');
    } catch (err) {
      setError(err instanceof ApiError ? err.detail || err.code : 'Không thể kết nối máy chủ xác thực.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-card">
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 18 }}>
          <span className="brand-dot">TM</span>
          <div>
            <h1 style={{ margin: 0, fontSize: 18 }}>TMusic Admin</h1>
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Cổng quản trị hệ thống TMusic</div>
          </div>
        </div>

        {error && (
          <div className="notice-banner notice-danger" style={{ marginBottom: 14 }} role="alert">
            {error}
          </div>
        )}

        <form onSubmit={handleLogin}>
          <div className="form-field">
            <label htmlFor="admin-email">Email quản trị</label>
            <input
              id="admin-email"
              className="input"
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="admin@domain.com"
            />
          </div>
          <div className="form-field">
            <label htmlFor="admin-password">Mật khẩu</label>
            <input
              id="admin-password"
              className="input"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <button className="btn btn-primary" style={{ width: '100%', marginTop: 8 }} disabled={busy}>
            <Lock size={15} />
            <span>{busy ? 'Đang đăng nhập...' : 'Đăng nhập'}</span>
          </button>
          <p style={{ marginTop: 14, fontSize: 12, color: 'var(--text-muted)', marginBottom: 0 }}>
            Phiên quản trị sử dụng HttpOnly cookie ngắn hạn kết hợp bảo vệ CSRF. Chỉ tài khoản có vai trò quản trị được phép đăng nhập.
          </p>
        </form>
      </div>
    </div>
  );
}
