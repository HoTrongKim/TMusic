import { useCallback, useEffect, useState } from 'react';
import { HardDrive, Trash2 } from 'lucide-react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { formatBytes, formatDateTime, Modal, SkeletonRows, StatusBadge } from '../components/UiKit';

interface StorageOverview {
  layers: {
    sqlServerDatabase: { label: string; bytes?: number | null; status: string; note: string };
    serverMp3Files: { label: string; bytes: number; fileCount: number; orphanBytes: number; orphanCount: number; referencedCount: number };
    serverCoverFiles: { label: string; bytes: number; fileCount: number; orphanBytes: number; orphanCount: number };
    objectStorage: { label: string; bytes?: number | null; status: string; note: string };
    tempAndQuarantine: { label: string; tempBytes: number; quarantineBytes: number; totalBytes: number };
    userDeviceFiles: { label: string; bytes?: number | null; status: string; note: string };
  };
  growthHistory: { available: boolean; note: string };
}

interface CleanupPreview {
  minAgeHours: number;
  candidateCount: number;
  reclaimableBytes: number;
  note: string;
  items: { category: string; userId: string; fileName: string; sizeBytes: number; lastModifiedUtc: string }[];
}

export function StoragePage() {
  const { hasPermission, notify } = useAuth();
  const [data, setData] = useState<StorageOverview | null>(null);
  const [preview, setPreview] = useState<CleanupPreview | null>(null);
  const [minAgeHours, setMinAgeHours] = useState(24);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await apiRequest<StorageOverview>('/api/admin/storage');
      setData(res);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải thông tin dung lượng.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [notify]);

  useEffect(() => {
    void load();
  }, [load]);

  const runPreview = async () => {
    setBusy(true);
    try {
      const res = await apiRequest<CleanupPreview>(`/api/admin/storage/cleanup/preview?minAgeHours=${minAgeHours}`);
      setPreview(res);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể quét tệp dư thừa.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const executeCleanup = async () => {
    setBusy(true);
    try {
      const res = await apiRequest<{ quarantinedCount: number; quarantinedBytes: number }>('/api/admin/storage/cleanup/execute', {
        method: 'POST',
        body: JSON.stringify({ confirm: true, minAgeHours, reason: reason.trim() || undefined }),
      });
      notify(`Đã cách ly ${res.quarantinedCount} tệp dư thừa (${formatBytes(res.quarantinedBytes)}).`, 'success');
      setConfirmOpen(false);
      setPreview(null);
      setReason('');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Dọn dẹp thất bại.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  if (loading && !data) return <div className="card"><SkeletonRows rows={6} /></div>;

  return (
    <>
      {data && (
        <div className="grid-2">
          <div className="card">
            <div className="card-header">
              <h3 className="card-title"><HardDrive size={15} style={{ marginRight: 6, verticalAlign: -2 }} />{data.layers.sqlServerDatabase.label}</h3>
              <StatusBadge status={data.layers.sqlServerDatabase.status} />
            </div>
            <div className="kpi-value">{formatBytes(data.layers.sqlServerDatabase.bytes)}</div>
            <p className="card-subtitle">{data.layers.sqlServerDatabase.note}</p>
          </div>

          <div className="card">
            <div className="card-header">
              <h3 className="card-title">{data.layers.serverMp3Files.label}</h3>
              <span className="badge badge-info">{data.layers.serverMp3Files.fileCount} tệp</span>
            </div>
            <div className="kpi-value" style={{ color: 'var(--accent)' }}>{formatBytes(data.layers.serverMp3Files.bytes)}</div>
            <p className="card-subtitle">
              Đang tham chiếu: {data.layers.serverMp3Files.referencedCount} · Không tham chiếu: {data.layers.serverMp3Files.orphanCount} ({formatBytes(data.layers.serverMp3Files.orphanBytes)})
            </p>
          </div>

          <div className="card">
            <div className="card-header">
              <h3 className="card-title">{data.layers.serverCoverFiles.label}</h3>
              <span className="badge badge-neutral">{data.layers.serverCoverFiles.fileCount} tệp</span>
            </div>
            <div className="kpi-value">{formatBytes(data.layers.serverCoverFiles.bytes)}</div>
            <p className="card-subtitle">
              Không còn tham chiếu: {data.layers.serverCoverFiles.orphanCount} ({formatBytes(data.layers.serverCoverFiles.orphanBytes)})
            </p>
          </div>

          <div className="card">
            <div className="card-header">
              <h3 className="card-title">{data.layers.tempAndQuarantine.label}</h3>
            </div>
            <div className="kpi-value">{formatBytes(data.layers.tempAndQuarantine.totalBytes)}</div>
            <p className="card-subtitle">
              Tệp tạm: {formatBytes(data.layers.tempAndQuarantine.tempBytes)} · Cách ly (_quarantine): {formatBytes(data.layers.tempAndQuarantine.quarantineBytes)}
            </p>
          </div>

          <div className="card">
            <div className="card-header">
              <h3 className="card-title">{data.layers.objectStorage.label}</h3>
              <StatusBadge status={data.layers.objectStorage.status} />
            </div>
            <div className="kpi-value">Chưa có dữ liệu</div>
            <p className="card-subtitle">{data.layers.objectStorage.note}</p>
          </div>

          <div className="card">
            <div className="card-header">
              <h3 className="card-title">{data.layers.userDeviceFiles.label}</h3>
              <StatusBadge status={data.layers.userDeviceFiles.status} />
            </div>
            <div className="kpi-value">Cục bộ trên thiết bị</div>
            <p className="card-subtitle">{data.layers.userDeviceFiles.note}</p>
          </div>
        </div>
      )}

      {hasPermission('storage.manage') && (
        <div className="card">
          <div className="card-header">
            <div>
              <h3 className="card-title">Dọn dẹp tệp dư thừa an toàn (Cách ly _quarantine)</h3>
              <p className="card-subtitle">
                Đối chiếu chéo toàn bộ tham chiếu SQL Server trước khi cách ly. Tuyệt đối không xóa tệp còn được người dùng tham chiếu và không bao giờ tác động tới SQL Server.
              </p>
            </div>
          </div>

          <div className="toolbar">
            <label style={{ fontSize: 13 }}>Tuổi tối thiểu của tệp mồ côi (giờ):</label>
            <input
              className="input"
              type="number"
              min={0}
              max={8760}
              value={minAgeHours}
              onChange={(e) => setMinAgeHours(Number(e.target.value))}
              style={{ width: 110 }}
            />
            <button className="btn btn-primary" disabled={busy} onClick={() => void runPreview()}>
              Xem trước tệp dư thừa
            </button>
          </div>

          {preview && (
            <div>
              <div className="notice-banner" style={{ marginBottom: 12 }}>
                {preview.note} Tìm thấy <strong>{preview.candidateCount}</strong> tệp ({formatBytes(preview.reclaimableBytes)}).
              </div>
              {preview.items.length > 0 && (
                <>
                  <div className="table-wrap" style={{ marginBottom: 14 }}>
                    <table>
                      <thead>
                        <tr><th>Loại</th><th>User</th><th>Tên tệp</th><th>Kích thước</th><th>Sửa lần cuối</th></tr>
                      </thead>
                      <tbody>
                        {preview.items.map((item) => (
                          <tr key={`${item.category}-${item.userId}-${item.fileName}`}>
                            <td><StatusBadge status={item.category} /></td>
                            <td className="mono">{item.userId.slice(0, 10)}…</td>
                            <td className="mono">{item.fileName}</td>
                            <td className="mono">{formatBytes(item.sizeBytes)}</td>
                            <td>{formatDateTime(item.lastModifiedUtc)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <button className="btn btn-danger" onClick={() => setConfirmOpen(true)}>
                    <Trash2 size={14} />
                    <span>Chuyển {preview.candidateCount} tệp dư thừa vào cách ly</span>
                  </button>
                </>
              )}
            </div>
          )}
        </div>
      )}

      {confirmOpen && preview && (
        <Modal title="Xác nhận cách ly tệp dư thừa" onClose={() => setConfirmOpen(false)}>
          <div className="notice-banner notice-warning" style={{ marginBottom: 12 }}>
            Bạn chuẩn bị di chuyển <strong>{preview.candidateCount}</strong> tệp không còn tham chiếu ({formatBytes(preview.reclaimableBytes)}) vào thư mục cách ly <code>_quarantine</code>. Hành động này sẽ được ghi vào Audit Log.
          </div>
          <div className="form-field">
            <label>Ghi chú Audit Log</label>
            <input className="input" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Dọn dẹp tệp mồ côi định kỳ" />
          </div>
          <div className="modal-footer">
            <button className="btn" onClick={() => setConfirmOpen(false)}>Hủy</button>
            <button className="btn btn-danger" disabled={busy} onClick={() => void executeCleanup()}>
              {busy ? 'Đang xử lý...' : 'Xác nhận cách ly'}
            </button>
          </div>
        </Modal>
      )}
    </>
  );
}
