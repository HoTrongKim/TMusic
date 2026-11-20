import { useCallback, useEffect, useState } from 'react';
import { UserPlus } from 'lucide-react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { formatDateTime, Modal, SkeletonRows, StatusBadge } from '../components/UiKit';

interface AdminItem {
  id: string;
  email: string;
  displayName: string;
  role: string;
  mfaEnabled: boolean;
  status: string;
  createdAt: string;
  lastActiveAt?: string | null;
}

interface InvitationItem {
  id: string;
  email: string;
  roleName: string;
  status: string;
  createdAt: string;
  expiresAt: string;
  acceptedAt?: string | null;
}

interface RecentAction {
  id: string;
  actorEmail?: string | null;
  action: string;
  targetType: string;
  result: string;
  reason?: string | null;
  createdAt: string;
}

export function AdminsPage() {
  const { notify } = useAuth();
  const [admins, setAdmins] = useState<AdminItem[]>([]);
  const [invitations, setInvitations] = useState<InvitationItem[]>([]);
  const [recentActions, setRecentActions] = useState<RecentAction[]>([]);
  const [loading, setLoading] = useState(true);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [inviteEmail, setInviteEmail] = useState('');
  const [inviteRole, setInviteRole] = useState('Admin');
  const [inviteNote, setInviteNote] = useState('');
  const [createdInvite, setCreatedInvite] = useState<{ email: string; acceptPath: string; emailDelivered: boolean; expiresAt: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [adminsRes, invitesRes] = await Promise.all([
        apiRequest<{ items: AdminItem[]; recentActions: RecentAction[] }>('/api/admin/admins'),
        apiRequest<{ items: InvitationItem[] }>('/api/admin/admins/invitations'),
      ]);
      setAdmins(adminsRes.items);
      setRecentActions(adminsRes.recentActions);
      setInvitations(invitesRes.items);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải danh sách quản trị viên.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [notify]);

  useEffect(() => {
    void load();
  }, [load]);

  const sendInvite = async () => {
    setBusy(true);
    try {
      const res = await apiRequest<{ email: string; acceptPath: string; emailDelivered: boolean; expiresAt: string }>(
        '/api/admin/admins/invite',
        {
          method: 'POST',
          body: JSON.stringify({ email: inviteEmail.trim(), roleName: inviteRole, note: inviteNote.trim() || undefined }),
        },
      );
      setCreatedInvite(res);
      setInviteEmail('');
      setInviteNote('');
      notify('Đã tạo lời mời quản trị dùng một lần.', 'success');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tạo lời mời.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const revokeInvite = async (id: string) => {
    try {
      await apiRequest(`/api/admin/admins/invitations/${id}/revoke`, { method: 'POST' });
      notify('Đã thu hồi lời mời.', 'success');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Thu hồi thất bại.', 'danger');
    }
  };

  const changeAdminRole = async (id: string, role: string) => {
    try {
      await apiRequest(`/api/admin/admins/${id}/role`, {
        method: 'PUT',
        body: JSON.stringify({ role, reason: `Cập nhật vai trò thành ${role}` }),
      });
      notify(`Đã cập nhật vai trò sang ${role}.`, 'success');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể đổi vai trò.', 'danger');
    }
  };

  const deactivateAdmin = async (id: string) => {
    const reason = window.prompt('Nhập lý do vô hiệu hóa tài khoản quản trị này:');
    if (!reason?.trim()) return;
    try {
      await apiRequest(`/api/admin/admins/${id}/deactivate`, {
        method: 'POST',
        body: JSON.stringify({ reason: reason.trim() }),
      });
      notify('Đã vô hiệu hóa tài khoản quản trị và thu hồi toàn bộ phiên.', 'success');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Vô hiệu hóa thất bại.', 'danger');
    }
  };

  return (
    <>
      <div className="card">
        <div className="card-header">
          <div>
            <h2 className="card-title">Quản lý Quản trị viên (SuperAdmin)</h2>
            <p className="card-subtitle">Mời quản trị viên bằng token dùng một lần có hạn sử dụng, bảo vệ SuperAdmin cuối cùng.</p>
          </div>
          <button className="btn btn-primary" onClick={() => { setCreatedInvite(null); setInviteOpen(true); }}>
            <UserPlus size={15} />
            <span>Mời quản trị viên</span>
          </button>
        </div>

        {loading ? (
          <SkeletonRows rows={5} />
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr><th>Quản trị viên</th><th>Vai trò</th><th>Trạng thái</th><th>Hoạt động cuối</th><th>Thao tác</th></tr>
              </thead>
              <tbody>
                {admins.map((a) => (
                  <tr key={a.id}>
                    <td>
                      <div style={{ fontWeight: 600 }}>{a.displayName}</div>
                      <div className="mono" style={{ color: 'var(--text-muted)' }}>{a.email}</div>
                    </td>
                    <td><StatusBadge status={a.role} /></td>
                    <td><StatusBadge status={a.status} /></td>
                    <td>{formatDateTime(a.lastActiveAt)}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                        <select
                          className="select"
                          style={{ width: 135, padding: '4px 8px', fontSize: 12.5 }}
                          value={a.role}
                          onChange={(e) => void changeAdminRole(a.id, e.target.value)}
                        >
                          <option value="SuperAdmin">SuperAdmin</option>
                          <option value="Admin">Admin</option>
                          <option value="Moderator">Moderator</option>
                          <option value="User">Thu hồi (User)</option>
                        </select>
                        {a.status === 'ACTIVE' && (
                          <button className="btn btn-sm btn-danger" onClick={() => void deactivateAdmin(a.id)}>
                            Vô hiệu hóa
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card">
        <h3 className="card-title" style={{ marginBottom: 12 }}>Lời mời quản trị đang theo dõi</h3>
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Email</th><th>Vai trò</th><th>Trạng thái</th><th>Hết hạn</th><th>Thao tác</th></tr>
            </thead>
            <tbody>
              {invitations.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Chưa có lời mời nào.</td></tr>
              ) : (
                invitations.map((inv) => (
                  <tr key={inv.id}>
                    <td className="mono">{inv.email}</td>
                    <td><StatusBadge status={inv.roleName} /></td>
                    <td><StatusBadge status={inv.status} /></td>
                    <td>{formatDateTime(inv.expiresAt)}</td>
                    <td>
                      {inv.status === 'PENDING' && (
                        <button className="btn btn-sm btn-danger" onClick={() => void revokeInvite(inv.id)}>
                          Thu hồi
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="card">
        <h3 className="card-title" style={{ marginBottom: 12 }}>Lịch sử thao tác quản trị gần nhất</h3>
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Thời gian</th><th>Quản trị viên</th><th>Hành động</th><th>Kết quả</th><th>Lý do</th></tr>
            </thead>
            <tbody>
              {recentActions.map((r) => (
                <tr key={r.id}>
                  <td>{formatDateTime(r.createdAt)}</td>
                  <td className="mono">{r.actorEmail || '—'}</td>
                  <td className="mono">{r.action}</td>
                  <td><StatusBadge status={r.result} /></td>
                  <td>{r.reason || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {inviteOpen && (
        <Modal title="Mời quản trị viên mới" onClose={() => setInviteOpen(false)}>
          {!createdInvite ? (
            <>
              <div className="notice-banner" style={{ marginBottom: 12 }}>
                Hệ thống tạo token ngẫu nhiên dùng một lần (hết hạn sau 24 giờ), không tạo hay gửi mật khẩu plaintext.
              </div>
              <div className="form-field">
                <label>Email người được mời</label>
                <input className="input" type="email" value={inviteEmail} onChange={(e) => setInviteEmail(e.target.value)} placeholder="ops@domain.com" />
              </div>
              <div className="form-field">
                <label>Vai trò cấp phát</label>
                <select className="select" value={inviteRole} onChange={(e) => setInviteRole(e.target.value)}>
                  <option value="Admin">Admin</option>
                  <option value="Moderator">Moderator</option>
                  <option value="SuperAdmin">SuperAdmin</option>
                </select>
              </div>
              <div className="form-field">
                <label>Ghi chú (tùy chọn)</label>
                <input className="input" value={inviteNote} onChange={(e) => setInviteNote(e.target.value)} />
              </div>
              <div className="modal-footer">
                <button className="btn" onClick={() => setInviteOpen(false)}>Hủy</button>
                <button className="btn btn-primary" disabled={busy || !inviteEmail.trim()} onClick={() => void sendInvite()}>
                  {busy ? 'Đang tạo lời mời...' : 'Tạo lời mời'}
                </button>
              </div>
            </>
          ) : (
            <div>
              <div className="notice-banner notice-warning" style={{ marginBottom: 12 }}>
                {createdInvite.emailDelivered
                  ? `Đã gửi email mời tới ${createdInvite.email}.`
                  : `SMTP chưa cấu hình — hãy gửi liên kết dùng một lần dưới đây cho ${createdInvite.email} qua kênh bảo mật.`}
              </div>
              <div className="form-field">
                <label>Đường dẫn chấp nhận lời mời (chỉ hiển thị 1 lần)</label>
                <div className="input mono" style={{ wordBreak: 'break-all', userSelect: 'all' }}>
                  {`${window.location.origin}${createdInvite.acceptPath}`}
                </div>
              </div>
              <div className="modal-footer">
                <button className="btn btn-primary" onClick={() => setInviteOpen(false)}>Đóng</button>
              </div>
            </div>
          )}
        </Modal>
      )}
    </>
  );
}
