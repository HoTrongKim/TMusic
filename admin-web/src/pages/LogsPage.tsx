import { useCallback, useEffect, useState } from 'react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { formatDateTime, Pagination, SkeletonRows, StatusBadge } from '../components/UiKit';

interface AuditRow {
  id: string;
  actorId?: string | null;
  actorEmail?: string | null;
  actorRole?: string | null;
  action: string;
  targetType: string;
  targetId?: string | null;
  result: string;
  reason?: string | null;
  correlationId?: string | null;
  createdAt: string;
}

interface EventRow {
  id: string;
  severity: string;
  eventType: string;
  code: string;
  message: string;
  correlationId?: string | null;
  createdAt: string;
}

export function LogsPage() {
  const { notify } = useAuth();
  const [tab, setTab] = useState<'audit' | 'events'>('audit');
  const [actionFilter, setActionFilter] = useState('');
  const [resultFilter, setResultFilter] = useState('');
  const [severityFilter, setSeverityFilter] = useState('');
  const [eventTypeFilter, setEventTypeFilter] = useState('');
  const [correlationId, setCorrelationId] = useState('');
  const [page, setPage] = useState(1);
  const [auditRows, setAuditRows] = useState<AuditRow[]>([]);
  const [eventRows, setEventRows] = useState<EventRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const qs = new URLSearchParams({ page: String(page), pageSize: '25' });
      if (correlationId.trim()) qs.set('correlationId', correlationId.trim());
      if (tab === 'audit') {
        if (actionFilter.trim()) qs.set('action', actionFilter.trim());
        if (resultFilter) qs.set('result', resultFilter);
        const res = await apiRequest<{ items: AuditRow[]; total: number }>(`/api/admin/audit-logs?${qs.toString()}`);
        setAuditRows(res.items);
        setTotal(res.total);
      } else {
        if (severityFilter) qs.set('severity', severityFilter);
        if (eventTypeFilter) qs.set('eventType', eventTypeFilter);
        const res = await apiRequest<{ items: EventRow[]; total: number }>(`/api/admin/system-events?${qs.toString()}`);
        setEventRows(res.items);
        setTotal(res.total);
      }
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải nhật ký hệ thống.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [tab, page, actionFilter, resultFilter, severityFilter, eventTypeFilter, correlationId, notify]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="card">
      <div className="card-header">
        <div>
          <h2 className="card-title">Nhật ký hệ thống & Bảo mật</h2>
          <p className="card-subtitle">
            Phân tách rõ Security Audit Logs (bất biến, lưu giữ 365 ngày) và Application System Events (đã làm sạch, không lộ secrets hay stack trace).
          </p>
        </div>
      </div>

      <div className="tabs">
        <button className={`tab-btn ${tab === 'audit' ? 'active' : ''}`} onClick={() => { setTab('audit'); setPage(1); }}>
          Security Audit Logs
        </button>
        <button className={`tab-btn ${tab === 'events' ? 'active' : ''}`} onClick={() => { setTab('events'); setPage(1); }}>
          Application System Events
        </button>
      </div>

      <div className="toolbar">
        <input className="input" placeholder="Correlation ID..." value={correlationId} onChange={(e) => { setCorrelationId(e.target.value); setPage(1); }} />
        {tab === 'audit' ? (
          <>
            <input className="input" placeholder="Lọc theo Action..." value={actionFilter} onChange={(e) => { setActionFilter(e.target.value); setPage(1); }} />
            <select className="select" value={resultFilter} onChange={(e) => { setResultFilter(e.target.value); setPage(1); }}>
              <option value="">Tất cả kết quả</option>
              <option value="SUCCESS">SUCCESS</option>
              <option value="FAILURE">FAILURE</option>
              <option value="DENIED">DENIED</option>
            </select>
          </>
        ) : (
          <>
            <select className="select" value={severityFilter} onChange={(e) => { setSeverityFilter(e.target.value); setPage(1); }}>
              <option value="">Tất cả mức độ</option>
              <option value="INFO">INFO</option>
              <option value="WARNING">WARNING</option>
              <option value="ERROR">ERROR</option>
              <option value="CRITICAL">CRITICAL</option>
            </select>
            <select className="select" value={eventTypeFilter} onChange={(e) => { setEventTypeFilter(e.target.value); setPage(1); }}>
              <option value="">Tất cả loại sự kiện</option>
              <option value="API_ERROR">API_ERROR</option>
              <option value="DOWNLOAD_ERROR">DOWNLOAD_ERROR</option>
              <option value="DB_ERROR">DB_ERROR</option>
              <option value="AUTH_FAILURE">AUTH_FAILURE</option>
              <option value="JOB_FAILURE">JOB_FAILURE</option>
              <option value="SYSTEM_HEALTH">SYSTEM_HEALTH</option>
            </select>
          </>
        )}
      </div>

      {loading ? (
        <SkeletonRows rows={6} />
      ) : tab === 'audit' ? (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr><th>Thời gian (UTC)</th><th>Người thực hiện</th><th>Hành động</th><th>Đối tượng</th><th>Kết quả</th><th>Lý do</th><th>Correlation</th></tr>
              </thead>
              <tbody>
                {auditRows.length === 0 ? (
                  <tr><td colSpan={7} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Chưa có bản ghi audit nào.</td></tr>
                ) : (
                  auditRows.map((a) => (
                    <tr key={a.id}>
                      <td>{formatDateTime(a.createdAt)}</td>
                      <td>
                        <div>{a.actorEmail || a.actorId?.slice(0, 8) || 'system'}</div>
                        {a.actorRole && <StatusBadge status={a.actorRole} />}
                      </td>
                      <td className="mono">{a.action}</td>
                      <td className="mono">{a.targetType}{a.targetId ? `:${a.targetId.slice(0, 10)}` : ''}</td>
                      <td><StatusBadge status={a.result} /></td>
                      <td>{a.reason || '—'}</td>
                      <td className="mono">{a.correlationId?.slice(0, 12) || '—'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={25} total={total} onPageChange={setPage} />
        </>
      ) : (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr><th>Thời gian</th><th>Mức độ</th><th>Loại</th><th>Mã</th><th>Thông điệp</th><th>Correlation</th></tr>
              </thead>
              <tbody>
                {eventRows.length === 0 ? (
                  <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Chưa có sự kiện hệ thống nào.</td></tr>
                ) : (
                  eventRows.map((e) => (
                    <tr key={e.id}>
                      <td>{formatDateTime(e.createdAt)}</td>
                      <td><StatusBadge status={e.severity} /></td>
                      <td className="mono">{e.eventType}</td>
                      <td className="mono">{e.code}</td>
                      <td>{e.message}</td>
                      <td className="mono">{e.correlationId?.slice(0, 12) || '—'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pageSize={25} total={total} onPageChange={setPage} />
        </>
      )}
    </div>
  );
}
