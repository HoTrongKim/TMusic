import { useCallback, useEffect, useState } from 'react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { formatBytes, formatDateTime, Modal, Pagination, SkeletonRows, StatusBadge } from '../components/UiKit';

interface SongRow {
  id: string;
  songId: string;
  ownerId: string;
  title: string;
  creator?: string | null;
  provider: string;
  canonicalUrl: string;
  audioStatus: string;
  serverFilePresent: boolean;
  storageScope: string;
  audioSizeBytes?: number | null;
  moderationStatus: string;
  moderationReason?: string | null;
  lastErrorCode?: string | null;
  openFlags: number;
  createdAt: string;
}

interface FlagRow {
  id: string;
  targetType: string;
  targetId: string;
  flagType: string;
  status: string;
  reason: string;
  createdAt: string;
}

export function SongsPage() {
  const { hasPermission, notify } = useAuth();
  const [tab, setTab] = useState<'songs' | 'flags'>('songs');
  const [q, setQ] = useState('');
  const [provider, setProvider] = useState('');
  const [audioStatus, setAudioStatus] = useState('');
  const [moderationStatus, setModerationStatus] = useState('');
  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<SongRow[]>([]);
  const [total, setTotal] = useState(0);
  const [notice, setNotice] = useState('');
  const [flags, setFlags] = useState<FlagRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState<{ mode: 'flag' | 'moderate'; song: SongRow } | null>(null);
  const [flagType, setFlagType] = useState('METADATA_ANOMALY');
  const [moderateAction, setModerateAction] = useState<'REMOVE' | 'RESTORE'>('REMOVE');
  const [quarantineAudio, setQuarantineAudio] = useState(true);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  const loadSongs = useCallback(async () => {
    setLoading(true);
    try {
      const qs = new URLSearchParams({ page: String(page), pageSize: '20' });
      if (q.trim()) qs.set('q', q.trim());
      if (provider) qs.set('provider', provider);
      if (audioStatus) qs.set('audioStatus', audioStatus);
      if (moderationStatus) qs.set('moderationStatus', moderationStatus);
      const res = await apiRequest<{ items: SongRow[]; total: number; notice: string }>(`/api/admin/songs?${qs.toString()}`);
      setRows(res.items);
      setTotal(res.total);
      setNotice(res.notice);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải danh sách bài hát.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [page, q, provider, audioStatus, moderationStatus, notify]);

  const loadFlags = useCallback(async () => {
    setLoading(true);
    try {
      const res = await apiRequest<{ items: FlagRow[] }>('/api/admin/moderation/flags?page=1&pageSize=50');
      setFlags(res.items);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải danh sách báo cáo.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [notify]);

  useEffect(() => {
    if (tab === 'songs') void loadSongs();
    else void loadFlags();
  }, [tab, loadSongs, loadFlags]);

  const submitModal = async () => {
    if (!modal) return;
    setBusy(true);
    try {
      if (modal.mode === 'flag') {
        await apiRequest(`/api/admin/songs/${modal.song.id}/flag`, {
          method: 'POST',
          body: JSON.stringify({ flagType, reason: reason.trim() }),
        });
        notify('Đã gắn cờ kiểm duyệt bài hát.', 'success');
      } else {
        await apiRequest(`/api/admin/songs/${modal.song.songId}/moderate`, {
          method: 'POST',
          body: JSON.stringify({ action: moderateAction, reason: reason.trim(), quarantineServerAudio: quarantineAudio }),
        });
        notify(moderateAction === 'REMOVE' ? 'Đã gỡ nội dung vi phạm và ghi audit log.' : 'Đã khôi phục trạng thái bài hát.', 'success');
      }
      setModal(null);
      setReason('');
      await loadSongs();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Thao tác thất bại.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const resolveFlag = async (id: string, status: 'RESOLVED' | 'DISMISSED') => {
    try {
      await apiRequest(`/api/admin/moderation/flags/${id}/resolve`, {
        method: 'POST',
        body: JSON.stringify({ status, note: status === 'RESOLVED' ? 'Đã xử lý' : 'Bỏ qua báo cáo' }),
      });
      notify('Đã cập nhật trạng thái báo cáo.', 'success');
      await loadFlags();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể cập nhật báo cáo.', 'danger');
    }
  };

  return (
    <div className="card">
      <div className="card-header">
        <div>
          <h2 className="card-title">Quản lý Bài hát & Nội dung</h2>
          <p className="card-subtitle">Giám sát metadata, trạng thái lưu trữ MP3 trên server và xử lý báo cáo vi phạm.</p>
        </div>
      </div>

      {notice && <div className="notice-banner" style={{ marginBottom: 14 }}>{notice}</div>}

      <div className="tabs">
        <button className={`tab-btn ${tab === 'songs' ? 'active' : ''}`} onClick={() => setTab('songs')}>
          Danh sách bài hát
        </button>
        <button className={`tab-btn ${tab === 'flags' ? 'active' : ''}`} onClick={() => setTab('flags')}>
          Hàng đợi kiểm duyệt & báo cáo
        </button>
      </div>

      {tab === 'songs' ? (
        <>
          <div className="toolbar">
            <input className="input" placeholder="Tìm ID, tiêu đề, nghệ sĩ, URL..." value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} />
            <select className="select" value={provider} onChange={(e) => { setProvider(e.target.value); setPage(1); }}>
              <option value="">Tất cả nguồn</option>
              <option value="youtube">YouTube</option>
              <option value="tiktok">TikTok</option>
              <option value="soundcloud">SoundCloud</option>
            </select>
            <select className="select" value={audioStatus} onChange={(e) => { setAudioStatus(e.target.value); setPage(1); }}>
              <option value="">Trạng thái tệp</option>
              <option value="READY">Có MP3 trên server</option>
              <option value="FAILED">Lỗi tải</option>
              <option value="NONE">Chỉ metadata / thiết bị</option>
            </select>
            <select className="select" value={moderationStatus} onChange={(e) => { setModerationStatus(e.target.value); setPage(1); }}>
              <option value="">Kiểm duyệt</option>
              <option value="ACTIVE">ACTIVE</option>
              <option value="FLAGGED">FLAGGED</option>
              <option value="REMOVED">REMOVED</option>
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
                      <th>Bài hát</th>
                      <th>Nguồn</th>
                      <th>Lưu trữ server</th>
                      <th>Kiểm duyệt</th>
                      <th>Lỗi gần nhất</th>
                      <th>Thời gian</th>
                      <th>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.length === 0 ? (
                      <tr><td colSpan={7} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Chưa có bản ghi bài hát nào.</td></tr>
                    ) : (
                      rows.map((s) => (
                        <tr key={s.id}>
                          <td>
                            <div style={{ fontWeight: 600 }}>{s.title}</div>
                            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{s.creator || '—'} · Owner <span className="mono">{s.ownerId.slice(0, 8)}</span></div>
                          </td>
                          <td>
                            <StatusBadge status={s.provider} />
                            <div style={{ fontSize: 11.5, marginTop: 3 }}>
                              <a href={s.canonicalUrl} target="_blank" rel="noreferrer">Mở nguồn</a>
                            </div>
                          </td>
                          <td>
                            <StatusBadge status={s.serverFilePresent ? 'READY' : s.audioStatus} />
                            <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 3 }}>
                              {s.serverFilePresent ? formatBytes(s.audioSizeBytes) : 'Không lưu tệp trên server'}
                            </div>
                          </td>
                          <td>
                            <StatusBadge status={s.moderationStatus} />
                            {s.openFlags > 0 && <div style={{ fontSize: 11.5, color: 'var(--warning)' }}>{s.openFlags} cờ đang mở</div>}
                          </td>
                          <td className="mono">{s.lastErrorCode || '—'}</td>
                          <td>{formatDateTime(s.createdAt)}</td>
                          <td>
                            {hasPermission('content.moderate') && (
                              <div style={{ display: 'flex', gap: 6 }}>
                                <button className="btn btn-sm" onClick={() => { setModal({ mode: 'flag', song: s }); setReason(''); }}>
                                  Gắn cờ
                                </button>
                                <button
                                  className={`btn btn-sm ${s.moderationStatus === 'REMOVED' ? '' : 'btn-danger'}`}
                                  onClick={() => {
                                    setModerateAction(s.moderationStatus === 'REMOVED' ? 'RESTORE' : 'REMOVE');
                                    setModal({ mode: 'moderate', song: s });
                                    setReason('');
                                  }}
                                >
                                  {s.moderationStatus === 'REMOVED' ? 'Khôi phục' : 'Gỡ bỏ'}
                                </button>
                              </div>
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
        </>
      ) : loading ? (
        <SkeletonRows rows={5} />
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Đối tượng</th><th>Loại cờ</th><th>Lý do</th><th>Trạng thái</th><th>Thời gian</th><th>Xử lý</th></tr>
            </thead>
            <tbody>
              {flags.length === 0 ? (
                <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Chưa có báo cáo kiểm duyệt nào.</td></tr>
              ) : (
                flags.map((f) => (
                  <tr key={f.id}>
                    <td><StatusBadge status={f.targetType} /> <span className="mono">{f.targetId.slice(0, 8)}</span></td>
                    <td>{f.flagType}</td>
                    <td>{f.reason}</td>
                    <td><StatusBadge status={f.status} /></td>
                    <td>{formatDateTime(f.createdAt)}</td>
                    <td>
                      {hasPermission('content.moderate') && f.status === 'OPEN' && (
                        <div style={{ display: 'flex', gap: 6 }}>
                          <button className="btn btn-sm btn-primary" onClick={() => void resolveFlag(f.id, 'RESOLVED')}>Xác nhận xử lý</button>
                          <button className="btn btn-sm" onClick={() => void resolveFlag(f.id, 'DISMISSED')}>Bỏ qua</button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {modal && (
        <Modal
          title={modal.mode === 'flag' ? 'Gắn cờ kiểm duyệt bài hát' : moderateAction === 'REMOVE' ? 'Gỡ nội dung vi phạm' : 'Khôi phục bài hát'}
          onClose={() => setModal(null)}
        >
          <p style={{ marginTop: 0, color: 'var(--text-muted)' }}>{modal.song.title}</p>
          {modal.mode === 'flag' && (
            <div className="form-field">
              <label>Loại gắn cờ</label>
              <select className="select" value={flagType} onChange={(e) => setFlagType(e.target.value)}>
                <option value="METADATA_ANOMALY">Metadata bất thường</option>
                <option value="VIOLATION_REPORT">Báo cáo vi phạm</option>
              </select>
            </div>
          )}
          {modal.mode === 'moderate' && moderateAction === 'REMOVE' && (
            <div className="notice-banner notice-warning" style={{ marginBottom: 12 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input type="checkbox" checked={quarantineAudio} onChange={(e) => setQuarantineAudio(e.target.checked)} />
                <span>Cách ly tệp MP3 lưu trên server liên quan tới bản ghi vi phạm này.</span>
              </label>
            </div>
          )}
          <div className="form-field">
            <label>Lý do (lưu Audit Log)</label>
            <textarea className="textarea" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          <div className="modal-footer">
            <button className="btn" onClick={() => setModal(null)}>Hủy</button>
            <button className="btn btn-primary" disabled={busy || !reason.trim()} onClick={() => void submitModal()}>
              {busy ? 'Đang xử lý...' : 'Xác nhận'}
            </button>
          </div>
        </Modal>
      )}
    </div>
  );
}
