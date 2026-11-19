import { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { ApiError, apiRequest } from '../api/client';
import { HorizontalDistChart, SimpleBarChart, StackedDownloadChart } from '../components/Charts';
import { formatBytes, KpiCard, SkeletonRows } from '../components/UiKit';

interface DashboardData {
  range: string;
  from: string;
  to: string;
  generatedAt: string;
  alerts: { openModerationFlags: number; recentErrors24h: number };
  kpis: {
    totalAccounts: { value: number; source: string };
    newUsersToday: { value: number; source: string };
    activeUsers7d: { value: number; source: string };
    activeUsers30d: { value: number; source: string };
    totalAlbums: { value: number; source: string };
    totalSavedSongs: { value: number; source: string };
    serverStoredMp3Count: { value: number; source: string };
    totalDownloadJobs: { value: number; source: string };
    completedDownloads: { value: number; source: string };
    failedDownloads: { value: number; source: string };
    activeDownloads: { value: number; source: string };
    serverStorageBytes: {
      value: number;
      audioBytes: number;
      coverBytes: number;
      audioFileCount: number;
      coverFileCount: number;
      source: string;
    };
  };
  charts: {
    registrationsByDay: { date: string; count: number }[];
    downloadsByDay: { date: string; total: number; succeeded: number; failed: number; canceled: number }[];
    downloadStatusRatio: { status: string; count: number }[];
    sourceDistribution: { provider: string; count: number }[];
    activeUsersByDay: { date: string; count: number }[];
    storageHistory: { available: boolean; reason: string };
  };
}

export function OverviewPage({ onAlertsUpdate }: { onAlertsUpdate?: (count: number) => void }) {
  const [range, setRange] = useState<'24h' | '7d' | '30d' | '90d' | 'custom'>('30d');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');
  const [data, setData] = useState<DashboardData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const qs = new URLSearchParams({ range });
      if (range === 'custom' && customFrom && customTo) {
        qs.set('from', new Date(customFrom).toISOString());
        qs.set('to', new Date(customTo).toISOString());
      }
      const res = await apiRequest<DashboardData>(`/api/admin/dashboard?${qs.toString()}`);
      setData(res);
      onAlertsUpdate?.(res.alerts.openModerationFlags + res.alerts.recentErrors24h);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail || err.code : 'Không thể tải dữ liệu tổng quan.');
    } finally {
      setLoading(false);
    }
  }, [range, customFrom, customTo, onAlertsUpdate]);

  useEffect(() => {
    if (range !== 'custom' || (customFrom && customTo)) {
      void load();
    }
  }, [load, range, customFrom, customTo]);

  return (
    <>
      <div className="card">
        <div className="card-header" style={{ marginBottom: 0 }}>
          <div>
            <h2 className="card-title">Tổng quan vận hành TMusic</h2>
            <p className="card-subtitle">
              Số liệu đo trực tiếp từ SQL Server và thư mục lưu trữ server — tách biệt hoàn toàn với tệp MP3 chỉ lưu trên thiết bị người dùng.
            </p>
          </div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
            {(['24h', '7d', '30d', '90d', 'custom'] as const).map((r) => (
              <button
                key={r}
                className={`btn btn-sm ${range === r ? 'btn-primary' : ''}`}
                onClick={() => setRange(r)}
              >
                {r === '24h' ? '24 giờ' : r === '7d' ? '7 ngày' : r === '30d' ? '30 ngày' : r === '90d' ? '90 ngày' : 'Tùy chỉnh'}
              </button>
            ))}
            <button className="btn btn-sm" onClick={() => void load()} disabled={loading}>
              <RefreshCw size={13} />
              <span>Làm mới</span>
            </button>
          </div>
        </div>
        {range === 'custom' && (
          <div className="toolbar" style={{ marginTop: 14, marginBottom: 0 }}>
            <input type="date" className="input" value={customFrom} onChange={(e) => setCustomFrom(e.target.value)} />
            <input type="date" className="input" value={customTo} onChange={(e) => setCustomTo(e.target.value)} />
            <button className="btn btn-sm btn-primary" disabled={!customFrom || !customTo} onClick={() => void load()}>
              Áp dụng
            </button>
          </div>
        )}
      </div>

      {error && <div className="notice-banner notice-danger">{error}</div>}
      {loading && !data && <div className="card"><SkeletonRows rows={6} /></div>}

      {data && (
        <>
          <div className="kpi-grid">
            <KpiCard label="Tổng tài khoản" value={data.kpis.totalAccounts.value.toLocaleString('vi-VN')} source={data.kpis.totalAccounts.source} />
            <KpiCard label="Người dùng mới hôm nay" value={data.kpis.newUsersToday.value.toLocaleString('vi-VN')} source={data.kpis.newUsersToday.source} accent />
            <KpiCard label="Hoạt động 7 ngày" value={data.kpis.activeUsers7d.value.toLocaleString('vi-VN')} source={data.kpis.activeUsers7d.source} />
            <KpiCard label="Hoạt động 30 ngày" value={data.kpis.activeUsers30d.value.toLocaleString('vi-VN')} source={data.kpis.activeUsers30d.source} />
            <KpiCard label="Tổng album" value={data.kpis.totalAlbums.value.toLocaleString('vi-VN')} source={data.kpis.totalAlbums.source} />
            <KpiCard label="Bài hát đã đồng bộ" value={data.kpis.totalSavedSongs.value.toLocaleString('vi-VN')} source={`MP3 lưu trên server: ${data.kpis.serverStoredMp3Count.value}`} />
            <KpiCard label="Tổng tác vụ tải nhạc" value={data.kpis.totalDownloadJobs.value.toLocaleString('vi-VN')} source={`Đang xử lý/chờ: ${data.kpis.activeDownloads.value}`} />
            <KpiCard label="Tải thành công" value={data.kpis.completedDownloads.value.toLocaleString('vi-VN')} source={data.kpis.completedDownloads.source} />
            <KpiCard label="Tải thất bại" value={data.kpis.failedDownloads.value.toLocaleString('vi-VN')} source={data.kpis.failedDownloads.source} />
            <KpiCard
              label="Dung lượng server sử dụng"
              value={formatBytes(data.kpis.serverStorageBytes.value)}
              source={`MP3: ${formatBytes(data.kpis.serverStorageBytes.audioBytes)} (${data.kpis.serverStorageBytes.audioFileCount} tệp) · Ảnh bìa: ${formatBytes(data.kpis.serverStorageBytes.coverBytes)}`}
              accent
            />
          </div>

          <div className="grid-2">
            <div className="card">
              <div className="card-header">
                <h3 className="card-title">Người dùng đăng ký theo ngày</h3>
              </div>
              <SimpleBarChart data={data.charts.registrationsByDay.map((x) => ({ label: x.date, value: x.count }))} />
            </div>

            <div className="card">
              <div className="card-header">
                <h3 className="card-title">Lượt tải MP3 theo thời gian</h3>
              </div>
              <StackedDownloadChart data={data.charts.downloadsByDay} />
            </div>

            <div className="card">
              <div className="card-header">
                <h3 className="card-title">Tỷ lệ trạng thái tải xuống</h3>
              </div>
              <HorizontalDistChart
                items={data.charts.downloadStatusRatio.map((x) => ({
                  label: x.status,
                  value: x.count,
                  color: x.status === 'COMPLETE' ? 'var(--success)' : x.status === 'FAILED' ? 'var(--danger)' : 'var(--accent)',
                }))}
              />
            </div>

            <div className="card">
              <div className="card-header">
                <h3 className="card-title">Phân bố nguồn liên kết</h3>
              </div>
              <HorizontalDistChart
                items={data.charts.sourceDistribution.map((x) => ({
                  label: x.provider,
                  value: x.count,
                }))}
              />
            </div>

            <div className="card">
              <div className="card-header">
                <h3 className="card-title">Người dùng hoạt động theo ngày</h3>
              </div>
              <SimpleBarChart
                data={data.charts.activeUsersByDay.map((x) => ({ label: x.date, value: x.count }))}
                color="var(--info)"
              />
            </div>

            <div className="card">
              <div className="card-header">
                <h3 className="card-title">Lịch sử tăng trưởng dung lượng lưu trữ</h3>
              </div>
              <div className="notice-banner">
                {data.charts.storageHistory.reason || 'Chưa có dữ liệu'}
              </div>
            </div>
          </div>
        </>
      )}
    </>
  );
}
