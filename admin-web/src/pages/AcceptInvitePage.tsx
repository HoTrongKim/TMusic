import React, { useEffect, useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import { AdminProfile, ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';

export function AcceptInvitePage({ token, onDone }: { token: string; onDone: () => void }) {
  const { setAdminSession, notify } = useAuth();
  const [inspect, setInspect] = useState<{
    email: string;
    roleName: string;
    expiresAt: string;
    existingAccount: boolean;
  } | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    apiRequest<{ email: string; roleName: string; expiresAt: string; existingAccount: boolean }>(
      `/api/admin/auth/invitations/inspect?token=${encodeURIComponent(token)}`,
      {},
      false,
    )
      .then(setInspect)
      .catch((err) => setError(err instanceof ApiError ? err.detail || err.code : 'Lời mời không hợp lệ hoặc đã hết hạn.'));
  }, [token]);

  const handleAccept = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const res = await apiRequest<{ csrfToken: string; admin: AdminProfile }>(
        '/api/admin/auth/invitations/accept',
        {
          method: 'POST',
          body: JSON.stringify({ token, password, displayName: displayName.trim() || undefined }),
        },
        false,
      );
      setPassword('');
      setAdminSession(res.admin, res.csrfToken);
      notify('Kích hoạt tài khoản quản trị thành công.', 'success');
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail || err.code : 'Không thể xác nhận lời mời.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-card">
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16 }}>
          <span className="brand-dot">TM</span>
          <div>
            <h1 style={{ margin: 0, fontSize: 18 }}>Xác nhận lời mời TMusic Admin</h1>
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Thiết lập tài khoản quản trị</div>
          </div>
        </div>

        {error && (
          <div className="notice-banner notice-danger" style={{ marginBottom: 14 }}>
            {error}
          </div>
        )}

        {!inspect && !error && <div className="skeleton" style={{ height: 120 }} />}

        {inspect && (
          <form onSubmit={handleAccept}>
            <div className="notice-banner" style={{ marginBottom: 14 }}>
              <div>
                <div>Email: <strong>{inspect.email}</strong></div>
                <div>Vai trò được cấp: <strong>{inspect.roleName}</strong></div>
              </div>
            </div>
            {!inspect.existingAccount && (
              <div className="form-field">
                <label>Tên hiển thị</label>
                <input className="input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Tên quản trị viên" />
              </div>
            )}
            <div className="form-field">
              <label>{inspect.existingAccount ? 'Xác nhận mật khẩu tài khoản hiện có' : 'Tạo mật khẩu mạnh (≥12 ký tự, hoa/thường/số/ký tự đặc biệt)'}</label>
              <input className="input" type="password" required value={password} onChange={(e) => setPassword(e.target.value)} />
            </div>
            <button className="btn btn-primary" style={{ width: '100%' }} disabled={busy}>
              <ShieldCheck size={15} />
              <span>{busy ? 'Đang kích hoạt...' : 'Hoàn tất & Vào Admin'}</span>
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
