import React from 'react';
import { ToastItem } from '../context/AuthContext';

export function formatBytes(bytes?: number | null): string {
  if (bytes === null || bytes === undefined) return 'Chưa có dữ liệu';
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let v = bytes;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(i === 0 ? 0 : 2)} ${units[i]}`;
}

export function formatDateTime(iso?: string | null): string {
  if (!iso) return 'Chưa có dữ liệu';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString('vi-VN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function KpiCard({
  label,
  value,
  source,
  accent,
}: {
  label: string;
  value: string | number;
  source?: string;
  accent?: boolean;
}) {
  return (
    <div className="kpi-card">
      <div className="kpi-label">{label}</div>
      <div className="kpi-value" style={accent ? { color: 'var(--accent)' } : undefined}>
        {value}
      </div>
      {source && <div className="kpi-source">{source}</div>}
    </div>
  );
}

export function StatusBadge({ status }: { status: string }) {
  const norm = status.toUpperCase();
  const cls =
    norm === 'ACTIVE' || norm === 'COMPLETED' || norm === 'COMPLETE' || norm === 'SUCCESS' || norm === 'READY' || norm === 'SYNCED' || norm === 'RESOLVED' || norm === 'AVAILABLE'
      ? 'badge-success'
      : norm === 'SUSPENDED' || norm === 'FAILED' || norm === 'REMOVED' || norm === 'DEACTIVATED' || norm === 'ERROR' || norm === 'CRITICAL' || norm === 'DENIED' || norm === 'REVOKED'
        ? 'badge-danger'
        : norm === 'QUEUED' || norm === 'PROCESSING' || norm === 'RUNNING' || norm === 'FLAGGED' || norm === 'OPEN' || norm === 'WARNING' || norm === 'PENDING'
          ? 'badge-warning'
          : norm === 'SUPERADMIN' || norm === 'ADMIN' || norm === 'INFO'
            ? 'badge-info'
            : 'badge-neutral';
  return <span className={`badge ${cls}`}>{status}</span>;
}

export function SkeletonRows({ rows = 5 }: { rows?: number }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="skeleton" style={{ height: 34, width: '100%' }} />
      ))}
    </div>
  );
}

export function Pagination({
  page,
  pageSize,
  total,
  onPageChange,
}: {
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (p: number) => void;
}) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="pagination">
      <span>
        Tổng <strong>{total.toLocaleString('vi-VN')}</strong> bản ghi — Trang {page}/{totalPages}
      </span>
      <div style={{ display: 'flex', gap: 8 }}>
        <button className="btn btn-sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Trước
        </button>
        <button className="btn btn-sm" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          Sau
        </button>
      </div>
    </div>
  );
}

export function Modal({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label={title} onClick={onClose}>
      <div className="modal-card" onClick={(e) => e.stopPropagation()}>
        <div className="card-header" style={{ marginBottom: 14 }}>
          <h3 className="card-title">{title}</h3>
          <button className="btn btn-sm" onClick={onClose} aria-label="Đóng">
            Đóng
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

export function ToastStack({ toasts }: { toasts: ToastItem[] }) {
  if (toasts.length === 0) return null;
  return (
    <div className="toast-stack" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} className={`toast ${t.tone === 'success' ? 'toast-success' : t.tone === 'danger' ? 'toast-danger' : ''}`}>
          {t.message}
        </div>
      ))}
    </div>
  );
}
