import { useCallback, useEffect, useState } from 'react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { formatDateTime, Modal, Pagination, SkeletonRows, StatusBadge } from '../components/UiKit';

interface UserRow {
  id: string;
  displayName: string;
  email: string;
  emailVerified: boolean;
  mfaEnabled: boolean;
  authMethods: string[];
  role: string;
  status: string;
  suspendedUntil?: string | null;
  suspensionReason?: string | null;
  createdAt: string;
  lastActiveAt?: string | null;
  albumCount: number;
  songCount: number;
}

interface UserDetail extends UserRow {
  stats: { albumCount: number; songCount: number; serverAudioCount: number; downloadCount: number; activeSessions: number };
  suspensionHistory: { id: string; action: string; reason: string; expiresAt?: string | null; createdAt: string }[];
}

export function UsersPage() {
  const { hasPermission, notify } = useAuth();
  const [q, setQ] = useState('');
  const [status, setStatus] = useState('');
  const [role, setRole] = useState('');
  const [sort, setSort] = useState('created_desc');
  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<UserRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<UserDetail | null>(null);
  const [actionModal, setActionModal] = useState<{ type: 'suspend' | 'reactivate' | 'role' | 'deactivate'; user: UserRow } | null>(null);
  const [reason, setReason] = useState('');
  const [durationHours, setDurationHours] = useState('');
  const [newRole, setNewRole] = useState('User');
  const [purgeFiles, setPurgeFiles] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const qs = new URLSearchParams({ page: String(page), pageSize: '20', sort });
      if (q.trim()) qs.set('q', q.trim());
      if (status) qs.set('status', status);
      if (role) qs.set('role', role);
      const res = await apiRequest<{ items: UserRow[]; total: number }>(`/api/admin/users?${qs.toString()}`);
      setRows(res.items);
      setTotal(res.total);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải danh sách người dùng.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [page, q, status, role, sort, notify]);

  useEffect(() => {
    void load();
  }, [load]);

  const openDetail = async (id: string) => {
    try {
      const detail = await apiRequest<UserDetail>(`/api/admin/users/${id}`);
      setSelected(detail);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải chi tiết người dùng.', 'danger');
    }
  };

  const runAction = async () => {
    if (!actionModal) return;
    setBusy(true);
    try {
      const { type, user } = actionModal;
      if (type === 'suspend') {
        await apiRequest(`/api/admin/users/${user.id}/suspend`, {
          method: 'POST',
          body: JSON.stringify({
            reason: reason.trim(),
            durationHours: durationHours ? Number(durationHours) : null,
          }),
        });
        notify('Đã tạm khóa tài khoản và thu hồi toàn bộ phiên đang hoạt động.', 'success');
      } else if (type === 'reactivate') {
        await apiRequest(`/api/admin/users/${user.id}/reactivate`, {
          method: 'POST',
          body: JSON.stringify({ reason: reason.trim() }),
        });
        notify('Đã mở khóa tài khoản.', 'success');
      } else if (type === 'role') {
        await apiRequest(`/api/admin/users/${user.id}/role`, {
          method: 'PUT',
          body: JSON.stringify({ role: newRole, reason: reason.trim() }),
        });
        notify(`Đã cập nhật vai trò sang ${newRole}.`, 'success');
      } else if (type === 'deactivate') {
        await apiRequest(`/api/admin/users/${user.id}/deactivate`, {
          method: 'POST',
          body: JSON.stringify({ confirm: true, reason: reason.trim(), purgeOwnedFiles: purgeFiles }),
        });
        notify('Đã vô hiệu hóa tài khoản an toàn.', 'success');
      }
      setActionModal(null);
      setReason('');
      setDurationHours('');
      setPurgeFiles(false);
      await load();
      if (selected && selected.id === user.id) {
        await openDetail(user.id);
      }
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Thao tác thất bại.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="card">
      <div className="card-header">
        <div>
          <h2 className="card-title">Quản lý người dùng</h2>
          <p className="card-subtitle">Tra cứu, kiểm tra trạng thái xác thực và xử lý vi phạm (không hiển thị mật khẩu hay token).</p>
        </div>
      </div>

      <div className="toolbar">
        <input
          className="input"
          placeholder="Tìm theo ID, tên hoặc email..."
          value={q}
          onChange={(e) => { setQ(e.target.value); setPage(1); }}
        />
        <select className="select" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
          <option value="">Tất cả trạng thái</option>
          <option value="active">Đang hoạt động</option>
          <option value="suspended">Tạm khóa</option>
          <option value="deactivated">Đã vô hiệu hóa</option>
        </select>
        <select className="select" value={role} onChange={(e) => { setRole(e.target.value); setPage(1); }}>
          <option value="">Tất cả vai trò</option>
          <option value="SuperAdmin">SuperAdmin</option>
          <option value="Admin">Admin</option>
          <option value="Moderator">Moderator</option>
          <option value="User">User</option>
        </select>
        <select className="select" value={sort} onChange={(e) => setSort(e.target.value)}>
          <option value="created_desc">Mới đăng ký nhất</option>
          <option value="created_asc">Đăng ký sớm nhất</option>
          <option value="active_desc">Hoạt động gần nhất</option>
          <option value="name_asc">Tên A–Z</option>
        </select>
      </div>

      {loading ? (
        <SkeletonRows rows={6} />
      ) : (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Tài khoản</th>
                  <th>Đăng nhập</th>
                  <th>Vai trò</th>
                  <th>Trạng thái</th>
                  <th>Album / Bài</th>
                  <th>Đăng ký</th>
                  <th>Hoạt động cuối</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {rows.length === 0 ? (
                  <tr>
                    <td colSpan={8} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>
                      Không tìm thấy tài khoản phù hợp.
                    </td>
                  </tr>
                ) : (
                  rows.map((u) => (
                    <tr key={u.id}>
                      <td>
                        <div style={{ fontWeight: 600 }}>{u.displayName || '(Chưa đặt tên)'}</div>
                        <div className="mono" style={{ color: 'var(--text-muted)' }}>{u.email}</div>
                      </td>
                      <td>{u.authMethods.join(', ') || '—'}</td>
                      <td><StatusBadge status={u.role} /></td>
                      <td><StatusBadge status={u.status} /></td>
                      <td className="mono">{u.albumCount} / {u.songCount}</td>
                      <td>{formatDateTime(u.createdAt)}</td>
                      <td>{formatDateTime(u.lastActiveAt)}</td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                          <button className="btn btn-sm" onClick={() => void openDetail(u.id)}>Chi tiết</button>
                          {hasPermission('users.suspend') && u.status === 'ACTIVE' && (
                            <button className="btn btn-sm btn-danger" onClick={() => { setActionModal({ type: 'suspend', user: u }); setReason(''); }}>
                              Tạm khóa
                            </button>
                          )}
                          {hasPermission('users.suspend') && u.status === 'SUSPENDED' && (
                            <button className="btn btn-sm" onClick={() => { setActionModal({ type: 'reactivate', user: u }); setReason(''); }}>
                              Mở khóa
                            </button>
                          )}
                          {hasPermission('roles.manage') && u.status !== 'DEACTIVATED' && (
                            <button className="btn btn-sm" onClick={() => { setActionModal({ type: 'role', user: u }); setNewRole(u.role); setReason(''); }}>
                              Đổi role
                            </button>
                          )}
                          {hasPermission('users.delete') && u.status !== 'DEACTIVATED' && (
                            <button className="btn btn-sm btn-danger" onClick={() => { setActionModal({ type: 'deactivate', user: u }); setReason(''); }}>
                              Vô hiệu hóa
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={20} total={total} onPageChange={setPage} />
        </>
      )}

      {selected && (
        <Modal title={`Chi tiết tài khoản — ${selected.displayName || selected.email}`} onClose={() => setSelected(null)}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, marginBottom: 14 }}>
            <div><span style={{ color: 'var(--text-muted)' }}>ID:</span><div className="mono">{selected.id}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Email:</span><div>{selected.email}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Xác minh email:</span><div>{selected.emailVerified ? 'Đã xác minh' : 'Chưa xác minh'}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>MFA:</span><div>{selected.mfaEnabled ? 'Đã bật' : 'Chưa bật'}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Vai trò:</span><div><StatusBadge status={selected.role} /></div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Trạng thái:</span><div><StatusBadge status={selected.status} /></div></div>
          </div>
          <div className="notice-banner" style={{ marginBottom: 14 }}>
            Album: <strong>{selected.stats.albumCount}</strong> · Bài hát đồng bộ: <strong>{selected.stats.songCount}</strong> · MP3 trên server: <strong>{selected.stats.serverAudioCount}</strong> · Phiên đang mở: <strong>{selected.stats.activeSessions}</strong>
          </div>
          <h4 style={{ margin: '10px 0 8px' }}>Lịch sử xử lý vi phạm / tạm khóa</h4>
          {selected.suspensionHistory.length === 0 ? (
            <div style={{ color: 'var(--text-muted)', fontSize: 13 }}>Chưa có lịch sử xử lý.</div>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr><th>Hành động</th><th>Lý do</th><th>Thời điểm</th></tr>
                </thead>
                <tbody>
                  {selected.suspensionHistory.map((h) => (
                    <tr key={h.id}>
                      <td><StatusBadge status={h.action} /></td>
                      <td>{h.reason}</td>
                      <td>{formatDateTime(h.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Modal>
      )}

      {actionModal && (
        <Modal
          title={
            actionModal.type === 'suspend' ? 'Tạm khóa tài khoản'
              : actionModal.type === 'reactivate' ? 'Mở khóa tài khoản'
                : actionModal.type === 'role' ? 'Thay đổi vai trò tài khoản'
                  : 'Xác nhận vô hiệu hóa tài khoản'
          }
          onClose={() => setActionModal(null)}
        >
          <p style={{ marginTop: 0, color: 'var(--text-muted)' }}>
            Đối tượng: <strong>{actionModal.user.email}</strong> (<span className="mono">{actionModal.user.id}</span>)
          </p>
          {actionModal.type === 'role' && (
            <div className="form-field">
              <label>Vai trò mới</label>
              <select className="select" value={newRole} onChange={(e) => setNewRole(e.target.value)}>
                <option value="User">User</option>
                <option value="Moderator">Moderator</option>
                <option value="Admin">Admin</option>
                <option value="SuperAdmin">SuperAdmin</option>
              </select>
            </div>
          )}
          {actionModal.type === 'suspend' && (
            <div className="form-field">
              <label>Thời hạn tạm khóa (giờ — để trống nếu vô thời hạn)</label>
              <input className="input" type="number" min={1} max={87600} value={durationHours} onChange={(e) => setDurationHours(e.target.value)} placeholder="VD: 24" />
            </div>
          )}
          {actionModal.type === 'deactivate' && (
            <div className="notice-banner notice-warning" style={{ marginBottom: 12 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input type="checkbox" checked={purgeFiles} onChange={(e) => setPurgeFiles(e.target.checked)} />
                <span>Xóa luôn tệp ảnh bìa / MP3 thuộc sở hữu riêng tài khoản này trên server (không ảnh hưởng dữ liệu chung của người dùng khác).</span>
              </label>
            </div>
          )}
          <div className="form-field">
            <label>Lý do (bắt buộc lưu Audit Log)</label>
            <textarea className="textarea" rows={3} required value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Nhập lý do rõ ràng..." />
          </div>
          <div className="modal-footer">
            <button className="btn" onClick={() => setActionModal(null)}>Hủy</button>
            <button
              className={`btn ${actionModal.type === 'suspend' || actionModal.type === 'deactivate' ? 'btn-danger' : 'btn-primary'}`}
              disabled={busy || !reason.trim()}
              onClick={() => void runAction()}
            >
              {busy ? 'Đang thực hiện...' : 'Xác nhận'}
            </button>
          </div>
        </Modal>
      )}
    </div>
  );
}
