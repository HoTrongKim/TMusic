import { useCallback, useEffect, useState } from 'react';
import { Download } from 'lucide-react';
import { apiDownloadBlob, ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { HorizontalDistChart } from '../components/Charts';
import { formatBytes, KpiCard, SkeletonRows } from '../components/UiKit';

interface AnalyticsData {
  range: string;
  userAnalytics: {
    newUsers: number;
    activeUsers: number;
    returningUsers: number;
    registrationMethods: { method: string; count: number }[];
  };
  musicAnalytics: {
    songsSaved: number;
    albumsCreated: number;
    downloadJobs: number;
    completedDownloads: number;
    failedDownloads: number;
    sourceDistribution: { provider: string; total: number; completed: number; failed: number }[];
  };
  infrastructureAnalytics: {
    apiErrorEventsCount: number;
    apiErrorRateNote: string;
    totalSystemEvents: number;
    downloadFailureRatePercent?: number | null;
    avgJobProcessingSeconds?: number | null;
    serverStorageBytes: number;
  };
}

export function AnalyticsPage() {
  const { notify } = useAuth();
  const [range, setRange] = useState<'7d' | '30d' | '90d'>('30d');
  const [data, setData] = useState<AnalyticsData | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await apiRequest<AnalyticsData>(`/api/admin/analytics?range=${range}`);
      setData(res);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải báo cáo phân tích.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [range, notify]);

  useEffect(() => {
    void load();
  }, [load]);

  const exportCsv = async (dataset: 'downloads' | 'registrations' | 'sources') => {
    try {
      await apiDownloadBlob(`/api/admin/analytics/export?dataset=${dataset}&range=${range}`, `tmusic-${dataset}-${range}.csv`);
      notify(`Đã xuất báo cáo CSV (${dataset}) và ghi Audit Log.`, 'success');
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Xuất CSV thất bại.', 'danger');
    }
  };

  return (
    <>
      <div className="card">
        <div className="card-header" style={{ marginBottom: 0 }}>
          <div>
            <h2 className="card-title">Thống kê & Báo cáo (Analytics)</h2>
            <p className="card-subtitle">Tổng hợp dữ liệu thật theo khoảng thời gian. Báo cáo CSV giới hạn tối đa 2.000 dòng, không chứa mật khẩu/token.</p>
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {(['7d', '30d', '90d'] as const).map((r) => (
              <button key={r} className={`btn btn-sm ${range === r ? 'btn-primary' : ''}`} onClick={() => setRange(r)}>
                {r === '7d' ? '7 ngày' : r === '30d' ? '30 ngày' : '90 ngày'}
              </button>
            ))}
            <button className="btn btn-sm" onClick={() => void exportCsv('downloads')}>
              <Download size={13} /> CSV Tải xuống
            </button>
            <button className="btn btn-sm" onClick={() => void exportCsv('registrations')}>
              <Download size={13} /> CSV Đăng ký
            </button>
            <button className="btn btn-sm" onClick={() => void exportCsv('sources')}>
              <Download size={13} /> CSV Nguồn
            </button>
          </div>
        </div>
      </div>

      {loading && !data ? (
        <div className="card"><SkeletonRows rows={6} /></div>
      ) : (
        data && (
          <>
            <div className="card">
              <h3 className="card-title" style={{ marginBottom: 14 }}>1. User Analytics</h3>
              <div className="kpi-grid" style={{ marginBottom: 16 }}>
                <KpiCard label="Người dùng mới" value={data.userAnalytics.newUsers} />
                <KpiCard label="Người dùng hoạt động" value={data.userAnalytics.activeUsers} accent />
                <KpiCard label="Người dùng quay lại" value={data.userAnalytics.returningUsers} />
              </div>
              <HorizontalDistChart items={data.userAnalytics.registrationMethods.map((m) => ({ label: m.method, value: m.count }))} />
            </div>

            <div className="card">
              <h3 className="card-title" style={{ marginBottom: 14 }}>2. Music Analytics</h3>
              <div className="kpi-grid" style={{ marginBottom: 16 }}>
                <KpiCard label="Bài hát đã lưu" value={data.musicAnalytics.songsSaved} />
                <KpiCard label="Album tạo mới" value={data.musicAnalytics.albumsCreated} />
                <KpiCard label="Tác vụ tải xuống" value={data.musicAnalytics.downloadJobs} />
                <KpiCard label="Tải thành công / lỗi" value={`${data.musicAnalytics.completedDownloads} / ${data.musicAnalytics.failedDownloads}`} />
              </div>
              <HorizontalDistChart items={data.musicAnalytics.sourceDistribution.map((s) => ({ label: `${s.provider} (OK ${s.completed}, lỗi ${s.failed})`, value: s.total }))} />
            </div>

            <div className="card">
              <h3 className="card-title" style={{ marginBottom: 14 }}>3. Infrastructure Analytics</h3>
              <div className="kpi-grid">
                <KpiCard label="Sự kiện API Error" value={data.infrastructureAnalytics.apiErrorEventsCount} source={data.infrastructureAnalytics.apiErrorRateNote} />
                <KpiCard
                  label="Tỷ lệ tải thất bại"
                  value={data.infrastructureAnalytics.downloadFailureRatePercent != null ? `${data.infrastructureAnalytics.downloadFailureRatePercent}%` : 'Chưa có dữ liệu'}
                />
                <KpiCard
                  label="Thời gian xử lý TB / job"
                  value={data.infrastructureAnalytics.avgJobProcessingSeconds != null ? `${data.infrastructureAnalytics.avgJobProcessingSeconds}s` : 'Chưa có dữ liệu'}
                />
                <KpiCard label="Dung lượng server hiện tại" value={formatBytes(data.infrastructureAnalytics.serverStorageBytes)} accent />
              </div>
            </div>
          </>
        )
      )}
    </>
  );
}
