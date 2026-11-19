import React, { useEffect, useState } from 'react';
import QRCode from 'qrcode';
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
  const [mfaSetup, setMfaSetup] = useState<{ sharedKey: string; otpAuthUri: string } | null>(null);
  const [qrUrl, setQrUrl] = useState('');
  const [code, setCode] = useState('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const [pendingSession, setPendingSession] = useState<{ admin: AdminProfile; csrf: string } | null>(null);
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

  useEffect(() => {
    if (!mfaSetup?.otpAuthUri) return;
    QRCode.toDataURL(mfaSetup.otpAuthUri, { width: 196, margin: 1 }).then(setQrUrl).catch(() => {});
  }, [mfaSetup]);

  const handleAccept = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const res = await apiRequest<{ sharedKey: string; otpAuthUri: string }>(
        '/api/admin/auth/invitations/accept',
        {
          method: 'POST',
          body: JSON.stringify({ token, password, displayName: displayName.trim() || undefined }),
        },
        false,
      );
      setMfaSetup(res);
      setPassword('');
    } catch (err) {
      setError(err instanceof ApiError ? err.detail || err.code : 'Không thể xác nhận lời mời.');
    } finally {
      setBusy(false);
    }
  };

  const handleCompleteMfa = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const res = await apiRequest<{ csrfToken: string; recoveryCodes: string[]; admin: AdminProfile }>(
        '/api/admin/auth/invitations/complete-mfa',
        {
          method: 'POST',
          body: JSON.stringify({ token, code: code.trim() }),
        },
        false,
      );
      setRecoveryCodes(res.recoveryCodes);
      setPendingSession({ admin: res.admin, csrf: res.csrfToken });
    } catch (err) {
      setError(err instanceof ApiError ? err.detail || err.code : 'Mã TOTP chưa đúng.');
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
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Thiết lập tài khoản & MFA bắt buộc</div>
          </div>
        </div>

        {error && (
          <div className="notice-banner notice-danger" style={{ marginBottom: 14 }}>
            {error}
          </div>
        )}

        {!inspect && !error && <div className="skeleton" style={{ height: 120 }} />}

        {inspect && !mfaSetup && !recoveryCodes && (
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
              {busy ? 'Đang xử lý...' : 'Tiếp tục thiết lập MFA'}
            </button>
          </form>
        )}

        {mfaSetup && !recoveryCodes && (
          <form onSubmit={handleCompleteMfa}>
            {qrUrl && (
              <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 12, background: '#fff', padding: 10, borderRadius: 8 }}>
                <img src={qrUrl} alt="QR TOTP" width={196} height={196} />
              </div>
            )}
            <div className="form-field">
              <label>Khóa bí mật TOTP</label>
              <div className="input mono">{mfaSetup.sharedKey}</div>
            </div>
            <div className="form-field">
              <label>Nhập mã 6 chữ số để hoàn tất cấp quyền</label>
              <input className="input mono" required value={code} onChange={(e) => setCode(e.target.value)} placeholder="123456" />
            </div>
            <button className="btn btn-primary" style={{ width: '100%' }} disabled={busy}>
              <ShieldCheck size={15} />
              <span>{busy ? 'Đang kích hoạt...' : 'Hoàn tất & Vào Admin'}</span>
            </button>
          </form>
        )}

        {recoveryCodes && pendingSession && (
          <div>
            <div className="notice-banner notice-warning" style={{ marginBottom: 12 }}>
              Lưu bộ 10 mã khôi phục MFA này ở nơi an toàn trước khi tiếp tục.
            </div>
            <div className="mono" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, padding: 12, background: 'var(--bg)', borderRadius: 8, marginBottom: 14 }}>
              {recoveryCodes.map((c) => <div key={c}>{c}</div>)}
            </div>
            <button
              className="btn btn-primary"
              style={{ width: '100%' }}
              onClick={() => {
                setAdminSession(pendingSession.admin, pendingSession.csrf);
                notify('Kích hoạt tài khoản quản trị thành công.', 'success');
                onDone();
              }}
            >
              Vào bảng điều khiển
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
