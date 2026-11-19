import { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { formatBytes, formatDateTime, Modal, Pagination, SkeletonRows, StatusBadge } from '../components/UiKit';

interface DownloadJobRow {
  id: string;
  userId: string;
  songId: string;
  provider: string;
  status: string;
  progressPercent: number;
  bitrateKbps?: number | null;
  outputSizeBytes?: number | null;
  errorCode?: string | null;
  retryCount: number;
  retryable: boolean;
  canCancel: boolean;
  durationSeconds?: number | null;
  createdAt: string;
  completedAt?: string | null;
}

interface PlatformStat {
  provider: string;
  total: number;
  queued: number;
  processing: number;
  completed: number;
  failed: number;
  cancelled: number;
}

export function DownloadsPage() {
  const { hasPermission, notify } = useAuth();
  const [status, setStatus] = useState('');
  const [provider, setProvider] = useState('');
  const [failedOnly, setFailedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<DownloadJobRow[]>([]);
  const [total, setTotal] = useState(0);
  const [platformStats, setPlatformStats] = useState<PlatformStat[]>([]);
  const [detail, setDetail] = useState<(DownloadJobRow & { songTitle?: string; canonicalUrl?: string }) | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const qs = new URLSearchParams({ page: String(page), pageSize: '20' });
      if (status) qs.set('status', status);
      if (provider) qs.set('provider', provider);
      if (failedOnly) qs.set('failedOnly', 'true');
      const res = await apiRequest<{ items: DownloadJobRow[]; total: number; platformStats: PlatformStat[] }>(`/api/admin/downloads?${qs.toString()}`);
      setRows(res.items);
      setTotal(res.total);
      setPlatformStats(res.platformStats);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải danh sách tác vụ tải xuống.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [page, status, provider, failedOnly, notify]);

  useEffect(() => {
    void load();
  }, [load]);

  const cancelJob = async (id: string) => {
    try {
      await apiRequest(`/api/admin/downloads/${id}/cancel`, { method: 'POST' });
      notify('Đã hủy tác vụ tải xuống.', 'success');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể hủy tác vụ.', 'danger');
    }
  };

  const retryJob = async (id: string) => {
    try {
      await apiRequest(`/api/admin/downloads/${id}/retry`, { method: 'POST' });
      notify('Đã đưa tác vụ vào hàng đợi thử lại.', 'success');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Tác vụ này không thuộc diện thử lại hợp lệ.', 'danger');
    }
  };

  const inspectJob = async (id: string) => {
    try {
      const d = await apiRequest<DownloadJobRow & { songTitle?: string; canonicalUrl?: string }>(`/api/admin/downloads/${id}`);
      setDetail(d);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải chi tiết tác vụ.', 'danger');
    }
  };

  return (
    <>
      {platformStats.length > 0 && (
        <div className="kpi-grid">
          {platformStats.map((p) => (
            <div key={p.provider} className="kpi-card">
              <div className="kpi-label">
                <span style={{ textTransform: 'capitalize', fontWeight: 600 }}>{p.provider}</span>
                <span className="mono">{p.total} jobs</span>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 4 }}>
                <span className="badge badge-success">OK: {p.completed}</span>
                <span className="badge badge-danger">Lỗi: {p.failed}</span>
                <span className="badge badge-warning">Đang chạy: {p.queued + p.processing}</span>
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="card">
        <div className="card-header">
          <div>
            <h2 className="card-title">Giám sát tác vụ tải âm thanh (MP3)</h2>
            <p className="card-subtitle">Theo dõi hàng đợi trích xuất âm thanh thực tế trên backend, mã lỗi đã làm sạch và chính sách thử lại hợp lệ.</p>
          </div>
          <button className="btn btn-sm" onClick={() => void load()}>
            <RefreshCw size={13} />
            <span>Làm mới</span>
          </button>
        </div>

        <div className="toolbar">
          <select className="select" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
            <option value="">Tất cả trạng thái</option>
            <option value="Queued">Queued</option>
            <option value="Processing">Processing</option>
            <option value="Completed">Completed</option>
            <option value="Failed">Failed</option>
            <option value="Cancelled">Cancelled</option>
          </select>
          <select className="select" value={provider} onChange={(e) => { setProvider(e.target.value); setPage(1); }}>
            <option value="">Tất cả nền tảng</option>
            <option value="youtube">YouTube</option>
            <option value="tiktok">TikTok</option>
            <option value="soundcloud">SoundCloud</option>
          </select>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
            <input type="checkbox" checked={failedOnly} onChange={(e) => { setFailedOnly(e.target.checked); setPage(1); }} />
            <span>Chỉ xem tác vụ thất bại</span>
          </label>
        </div>

        {loading ? (
          <SkeletonRows rows={6} />
        ) : (
          <>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Job ID</th>
                    <th>Nền tảng</th>
                    <th>Trạng thái</th>
                    <th>Thời lượng</th>
                    <th>Đầu ra</th>
                    <th>Mã lỗi / Retry</th>
                    <th>Tạo lúc</th>
                    <th>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 ? (
                    <tr><td colSpan={8} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Chưa có tác vụ tải xuống nào.</td></tr>
                  ) : (
                    rows.map((j) => (
                      <tr key={j.id}>
                        <td className="mono">{j.id.slice(0, 10)}…</td>
                        <td><StatusBadge status={j.provider} /></td>
                        <td><StatusBadge status={j.status} /></td>
                        <td className="mono">{j.durationSeconds != null ? `${j.durationSeconds}s` : '—'}</td>
                        <td className="mono">
                          {j.outputSizeBytes != null ? formatBytes(j.outputSizeBytes) : '—'}
                          {j.bitrateKbps ? ` (${j.bitrateKbps}k)` : ''}
                        </td>
                        <td>
                          <span className="mono" style={{ color: j.errorCode ? 'var(--danger)' : 'var(--text-muted)' }}>
                            {j.errorCode || '—'}
                          </span>
                          {j.retryCount > 0 && <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>Thử lại: {j.retryCount}</div>}
                        </td>
                        <td>{formatDateTime(j.createdAt)}</td>
                        <td>
                          <div style={{ display: 'flex', gap: 6 }}>
                            <button className="btn btn-sm" onClick={() => void inspectJob(j.id)}>Chi tiết</button>
                            {hasPermission('downloads.manage') && j.canCancel && (
                              <button className="btn btn-sm btn-danger" onClick={() => void cancelJob(j.id)}>Hủy</button>
                            )}
                            {hasPermission('downloads.manage') && j.retryable && (
                              <button className="btn btn-sm btn-primary" onClick={() => void retryJob(j.id)}>Thử lại</button>
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
      </div>

      {detail && (
        <Modal title={`Chi tiết tác vụ tải — ${detail.id.slice(0, 12)}`} onClose={() => setDetail(null)}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            <div><span style={{ color: 'var(--text-muted)' }}>Job ID:</span><div className="mono">{detail.id}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>User ID:</span><div className="mono">{detail.userId}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Bài hát:</span><div>{detail.songTitle || '—'}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Trạng thái:</span><div><StatusBadge status={detail.status} /></div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Nền tảng:</span><div>{detail.provider}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Bitrate / Size:</span><div className="mono">{detail.bitrateKbps ?? '—'} kbps / {formatBytes(detail.outputSizeBytes)}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Tạo lúc:</span><div>{formatDateTime(detail.createdAt)}</div></div>
            <div><span style={{ color: 'var(--text-muted)' }}>Hoàn tất:</span><div>{formatDateTime(detail.completedAt)}</div></div>
          </div>
          {detail.canonicalUrl && (
            <div style={{ marginTop: 12 }}>
              <span style={{ color: 'var(--text-muted)' }}>URL chuẩn hóa:</span>
              <div className="mono" style={{ wordBreak: 'break-all' }}>{detail.canonicalUrl}</div>
            </div>
          )}
          {detail.errorCode && (
            <div className="notice-banner notice-danger" style={{ marginTop: 12 }}>
              Mã lỗi đã làm sạch: <strong className="mono">{detail.errorCode}</strong>
            </div>
          )}
        </Modal>
      )}
    </>
  );
}
