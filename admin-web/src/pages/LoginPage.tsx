import React, { useEffect, useState } from 'react';
import QRCode from 'qrcode';
import { ShieldCheck, KeyRound, Lock } from 'lucide-react';
import { AdminProfile, ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';

interface LoginStepResponse {
  status: 'MFA_REQUIRED' | 'MFA_SETUP_REQUIRED';
  challengeToken: string;
  expiresAt: string;
}

interface MfaSetupResponse {
  sharedKey: string;
  rawKey: string;
  otpAuthUri: string;
}

interface MfaVerifyResponse {
  csrfToken: string;
  recoveryCodes?: string[] | null;
  admin: AdminProfile;
}

export function LoginPage() {
  const { setAdminSession, notify } = useAuth();
  const [step, setStep] = useState<'credentials' | 'mfa_verify' | 'mfa_setup' | 'recovery_codes'>('credentials');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [challengeToken, setChallengeToken] = useState('');
  const [mfaCode, setMfaCode] = useState('');
  const [setupInfo, setSetupInfo] = useState<MfaSetupResponse | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string>('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [pendingAdmin, setPendingAdmin] = useState<{ admin: AdminProfile; csrf: string } | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!setupInfo?.otpAuthUri) {
      setQrDataUrl('');
      return;
    }
    QRCode.toDataURL(setupInfo.otpAuthUri, { width: 196, margin: 1 })
      .then(setQrDataUrl)
      .catch(() => setQrDataUrl(''));
  }, [setupInfo]);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const res = await apiRequest<LoginStepResponse>(
        '/api/admin/auth/login',
        {
          method: 'POST',
          body: JSON.stringify({ email: email.trim(), password, deviceName: 'TMusic Admin Web' }),
        },
        false,
      );
      setChallengeToken(res.challengeToken);
      setPassword('');

      if (res.status === 'MFA_SETUP_REQUIRED') {
        const setup = await apiRequest<MfaSetupResponse>(
          '/api/admin/auth/mfa/setup',
          { method: 'POST', body: JSON.stringify({ challengeToken: res.challengeToken }) },
          false,
        );
        setSetupInfo(setup);
        setStep('mfa_setup');
      } else {
        setStep('mfa_verify');
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.detail || err.code : 'Không thể kết nối máy chủ xác thực.');
    } finally {
      setBusy(false);
    }
  };

  const handleVerifyMfa = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const res = await apiRequest<MfaVerifyResponse>(
        '/api/admin/auth/mfa/verify',
        {
          method: 'POST',
          body: JSON.stringify({ challengeToken, code: mfaCode.trim() }),
        },
        false,
      );
      if (res.recoveryCodes && res.recoveryCodes.length > 0) {
        setRecoveryCodes(res.recoveryCodes);
        setPendingAdmin({ admin: res.admin, csrf: res.csrfToken });
        setStep('recovery_codes');
      } else {
        setAdminSession(res.admin, res.csrfToken);
        notify('Đăng nhập TMusic Admin thành công.', 'success');
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.detail || err.code : 'Xác thực MFA thất bại.');
    } finally {
      setBusy(false);
    }
  };

  const completeRecoveryView = () => {
    if (pendingAdmin) {
      setAdminSession(pendingAdmin.admin, pendingAdmin.csrf);
      notify('Đã kích hoạt MFA và đăng nhập TMusic Admin.', 'success');
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-card">
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 18 }}>
          <span className="brand-dot">TM</span>
          <div>
            <h1 style={{ margin: 0, fontSize: 18 }}>TMusic Admin</h1>
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>Cổng quản trị hệ thống bảo mật MFA</div>
          </div>
        </div>

        {error && (
          <div className="notice-banner notice-danger" style={{ marginBottom: 14 }} role="alert">
            {error}
          </div>
        )}

        {step === 'credentials' && (
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
              <span>{busy ? 'Đang xác thực...' : 'Tiếp tục tới bước MFA'}</span>
            </button>
            <p style={{ marginTop: 14, fontSize: 12, color: 'var(--text-muted)', marginBottom: 0 }}>
              Phiên quản trị sử dụng HttpOnly cookie ngắn hạn kết hợp bảo vệ CSRF. Chỉ tài khoản có vai trò quản trị được phép đăng nhập.
            </p>
          </form>
        )}

        {step === 'mfa_setup' && setupInfo && (
          <form onSubmit={handleVerifyMfa}>
            <div className="notice-banner notice-warning" style={{ marginBottom: 14 }}>
              <ShieldCheck size={17} style={{ flexShrink: 0, marginTop: 2 }} />
              <span>Tài khoản quản trị bắt buộc thiết lập xác thực 2 bước (TOTP Authenticator) trước khi truy cập bảng điều khiển.</span>
            </div>
            {qrDataUrl && (
              <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 14, background: '#fff', padding: 10, borderRadius: 8 }}>
                <img src={qrDataUrl} alt="Mã QR thiết lập TOTP" width={196} height={196} />
              </div>
            )}
            <div className="form-field">
              <label>Khóa nhập thủ công (Authenticator App)</label>
              <div className="input mono" style={{ userSelect: 'all' }}>
                {setupInfo.sharedKey}
              </div>
            </div>
            <div className="form-field">
              <label htmlFor="mfa-setup-code">Nhập mã 6 chữ số từ ứng dụng Authenticator</label>
              <input
                id="mfa-setup-code"
                className="input mono"
                inputMode="numeric"
                required
                value={mfaCode}
                onChange={(e) => setMfaCode(e.target.value)}
                placeholder="123456"
              />
            </div>
            <button className="btn btn-primary" style={{ width: '100%' }} disabled={busy}>
              <KeyRound size={15} />
              <span>{busy ? 'Đang kích hoạt MFA...' : 'Xác nhận & Kích hoạt MFA'}</span>
            </button>
          </form>
        )}

        {step === 'mfa_verify' && (
          <form onSubmit={handleVerifyMfa}>
            <div className="form-field">
              <label htmlFor="mfa-verify-code">Mã xác thực 6 chữ số (hoặc mã khôi phục)</label>
              <input
                id="mfa-verify-code"
                className="input mono"
                autoComplete="one-time-code"
                required
                value={mfaCode}
                onChange={(e) => setMfaCode(e.target.value)}
                placeholder="123456"
              />
            </div>
            <button className="btn btn-primary" style={{ width: '100%' }} disabled={busy}>
              <ShieldCheck size={15} />
              <span>{busy ? 'Đang kiểm tra...' : 'Xác thực MFA & Đăng nhập'}</span>
            </button>
            <button
              type="button"
              className="btn"
              style={{ width: '100%', marginTop: 8 }}
              onClick={() => {
                setStep('credentials');
                setMfaCode('');
                setError('');
              }}
            >
              Quay lại
            </button>
          </form>
        )}

        {step === 'recovery_codes' && (
          <div>
            <div className="notice-banner notice-warning" style={{ marginBottom: 14 }}>
              <span>Lưu 10 mã khôi phục dùng một lần dưới đây ở nơi an toàn. Bạn sẽ không thể xem lại các mã này sau khi đóng màn hình.</span>
            </div>
            <div
              className="mono"
              style={{
                display: 'grid',
                gridTemplateColumns: '1fr 1fr',
                gap: 8,
                padding: 12,
                borderRadius: 8,
                background: 'var(--bg)',
                border: '1px solid var(--border)',
                marginBottom: 16,
              }}
            >
              {recoveryCodes.map((c) => (
                <div key={c}>{c}</div>
              ))}
            </div>
            <button className="btn btn-primary" style={{ width: '100%' }} onClick={completeRecoveryView}>
              Tôi đã lưu mã khôi phục — Vào trang quản trị
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
