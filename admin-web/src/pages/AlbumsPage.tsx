import { useCallback, useEffect, useState } from 'react';
import { ShieldAlert } from 'lucide-react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { formatDateTime, KpiCard, Modal, Pagination, SkeletonRows, StatusBadge } from '../components/UiKit';

interface AlbumRow {
  id: string;
  ownerId: string;
  name: string;
  songCount: number;
  activeShareCount: number;
  openFlagCount: number;
  syncState: string;
  hasIssue: boolean;
  createdAt: string;
  updatedAt: string;
}

interface IntegrityReport {
  healthy: boolean;
  totalIssues: number;
  checks: { code: string; label: string; count: number }[];
}

export function AlbumsPage() {
  const { hasPermission, notify } = useAuth();
  const [q, setQ] = useState('');
  const [onlyIssues, setOnlyIssues] = useState(false);
  const [page, setPage] = useState(1);
  const [summary, setSummary] = useState({ totalAlbums: 0, created7d: 0, softDeletedCount: 0 });
  const [rows, setRows] = useState<AlbumRow[]>([]);
  const [total, setTotal] = useState(0);
  const [integrity, setIntegrity] = useState<IntegrityReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [flagAlbum, setFlagAlbum] = useState<AlbumRow | null>(null);
  const [reason, setReason] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const qs = new URLSearchParams({ page: String(page), pageSize: '20' });
      if (q.trim()) qs.set('q', q.trim());
      if (onlyIssues) qs.set('onlyIssues', 'true');
      const [albumsRes, integrityRes] = await Promise.all([
        apiRequest<{ summary: typeof summary; items: AlbumRow[]; total: number }>(`/api/admin/albums?${qs.toString()}`),
        apiRequest<IntegrityReport>('/api/admin/albums/integrity'),
      ]);
      setSummary(albumsRes.summary);
      setRows(albumsRes.items);
      setTotal(albumsRes.total);
      setIntegrity(integrityRes);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải dữ liệu album.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [page, q, onlyIssues, notify]);

  useEffect(() => {
    void load();
  }, [load]);

  const submitFlag = async () => {
    if (!flagAlbum) return;
    try {
      await apiRequest(`/api/admin/albums/${flagAlbum.id}/flag`, {
        method: 'POST',
        body: JSON.stringify({ reason: reason.trim() }),
      });
      notify('Đã tạo báo cáo kiểm duyệt cho album và lưu audit log.', 'success');
      setFlagAlbum(null);
      setReason('');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể gắn cờ album.', 'danger');
    }
  };

  return (
    <>
      <div className="kpi-grid">
        <KpiCard label="Tổng album đang hoạt động" value={summary.totalAlbums.toLocaleString('vi-VN')} />
        <KpiCard label="Album tạo mới (7 ngày)" value={summary.created7d.toLocaleString('vi-VN')} accent />
        <KpiCard label="Album xóa mềm (chờ dọn)" value={summary.softDeletedCount.toLocaleString('vi-VN')} />
        <KpiCard label="Bất thường toàn vẹn dữ liệu" value={integrity ? integrity.totalIssues : '—'} />
      </div>

      {integrity && (
        <div className="card">
          <div className="card-header">
            <div>
              <h3 className="card-title">Chẩn đoán toàn vẹn Album & Liên kết bài hát</h3>
              <p className="card-subtitle">Phát hiện dữ liệu mồ côi, liên kết chia sẻ trên album đã xóa mềm và báo cáo mở (không chỉnh sửa tùy tiện album cá nhân).</p>
            </div>
            <StatusBadge status={integrity.healthy ? 'ACTIVE' : 'WARNING'} />
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 10 }}>
            {integrity.checks.map((c) => (
              <div key={c.code} style={{ padding: 12, borderRadius: 8, background: 'var(--bg)', border: '1px solid var(--border)' }}>
                <div style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>{c.label}</div>
                <div style={{ fontSize: 20, fontWeight: 700, marginTop: 4, color: c.count > 0 ? 'var(--warning)' : 'var(--success)' }}>
                  {c.count}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="card">
        <div className="toolbar">
          <input className="input" placeholder="Tìm theo Album ID, Owner ID hoặc tên..." value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} />
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
            <input type="checkbox" checked={onlyIssues} onChange={(e) => { setOnlyIssues(e.target.checked); setPage(1); }} />
            <span>Chỉ hiển thị album có cảnh báo / xóa mềm</span>
          </label>
        </div>

        {loading ? (
          <SkeletonRows rows={6} />
        ) : (
          <>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr><th>Album</th><th>Chủ sở hữu</th><th>Số bài</th><th>Chia sẻ</th><th>Đồng bộ</th><th>Cập nhật</th><th>Thao tác</th></tr>
                </thead>
                <tbody>
                  {rows.length === 0 ? (
                    <tr><td colSpan={7} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Không có album nào khớp bộ lọc.</td></tr>
                  ) : (
                    rows.map((a) => (
                      <tr key={a.id}>
                        <td>
                          <div style={{ fontWeight: 600 }}>{a.name || '(Không tên)'}</div>
                          <div className="mono" style={{ color: 'var(--text-muted)' }}>{a.id}</div>
                        </td>
                        <td className="mono">{a.ownerId.slice(0, 12)}…</td>
                        <td className="mono">{a.songCount}</td>
                        <td className="mono">{a.activeShareCount}</td>
                        <td>
                          <StatusBadge status={a.syncState} />
                          {a.openFlagCount > 0 && <span className="badge badge-warning" style={{ marginLeft: 6 }}>{a.openFlagCount} cờ</span>}
                        </td>
                        <td>{formatDateTime(a.updatedAt)}</td>
                        <td>
                          {hasPermission('content.moderate') && (
                            <button className="btn btn-sm" onClick={() => { setFlagAlbum(a); setReason(''); }}>
                              <ShieldAlert size={13} />
                              <span>Báo cáo</span>
                            </button>
                          )}
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
      </div>

      {flagAlbum && (
        <Modal title={`Ghi nhận báo cáo cho album "${flagAlbum.name}"`} onClose={() => setFlagAlbum(null)}>
          <div className="form-field">
            <label>Lý do kiểm duyệt (ghi Audit Log)</label>
            <textarea className="textarea" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          <div className="modal-footer">
            <button className="btn" onClick={() => setFlagAlbum(null)}>Hủy</button>
            <button className="btn btn-primary" disabled={!reason.trim()} onClick={() => void submitFlag()}>Xác nhận</button>
          </div>
        </Modal>
      )}
    </>
  );
}
