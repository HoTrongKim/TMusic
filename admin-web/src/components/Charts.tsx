export function SimpleBarChart({
  data,
  emptyLabel = 'Chưa có dữ liệu trong khoảng thời gian này.',
  color = 'var(--accent)',
}: {
  data: { label: string; value: number }[];
  emptyLabel?: string;
  color?: string;
}) {
  if (data.length === 0) {
    return <div className="notice-banner">{emptyLabel}</div>;
  }

  const max = Math.max(1, ...data.map((d) => d.value));
  const width = 560;
  const height = 190;
  const padBottom = 28;
  const padTop = 18;
  const plotH = height - padBottom - padTop;
  const step = width / data.length;
  const barW = Math.max(8, Math.min(32, step * 0.62));

  return (
    <div style={{ width: '100%', overflowX: 'auto' }}>
      <svg viewBox={`0 0 ${width} ${height}`} style={{ width: '100%', height: 190, display: 'block' }} role="img">
        <line x1={0} y1={padTop + plotH} x2={width} y2={padTop + plotH} stroke="var(--border)" strokeWidth="1" />
        {data.map((d, idx) => {
          const barH = Math.max(2, (d.value / max) * plotH);
          const x = idx * step + (step - barW) / 2;
          const y = padTop + plotH - barH;
          const showTick = data.length <= 10 || idx === 0 || idx === data.length - 1 || idx % Math.ceil(data.length / 6) === 0;
          return (
            <g key={d.label}>
              <title>{`${d.label}: ${d.value}`}</title>
              <rect x={x} y={y} width={barW} height={barH} rx={3} fill={color} opacity={0.9} />
              {data.length <= 12 && (
                <text x={x + barW / 2} y={y - 4} textAnchor="middle" fill="var(--text-muted)" fontSize="10">
                  {d.value}
                </text>
              )}
              {showTick && (
                <text x={x + barW / 2} y={height - 8} textAnchor="middle" fill="var(--text-muted)" fontSize="10">
                  {d.label.slice(5)}
                </text>
              )}
            </g>
          );
        })}
      </svg>
    </div>
  );
}

export function StackedDownloadChart({
  data,
}: {
  data: { date: string; succeeded: number; failed: number; canceled: number }[];
}) {
  if (data.length === 0) {
    return <div className="notice-banner">Chưa có tác vụ tải xuống trong khoảng thời gian đã chọn.</div>;
  }

  const max = Math.max(1, ...data.map((d) => d.succeeded + d.failed + d.canceled));
  const width = 560;
  const height = 190;
  const padBottom = 28;
  const padTop = 14;
  const plotH = height - padBottom - padTop;
  const step = width / data.length;
  const barW = Math.max(8, Math.min(30, step * 0.6));

  return (
    <div>
      <svg viewBox={`0 0 ${width} ${height}`} style={{ width: '100%', height: 190, display: 'block' }} role="img">
        <line x1={0} y1={padTop + plotH} x2={width} y2={padTop + plotH} stroke="var(--border)" strokeWidth="1" />
        {data.map((d, idx) => {
          const x = idx * step + (step - barW) / 2;
          const okH = (d.succeeded / max) * plotH;
          const failH = (d.failed / max) * plotH;
          const cancelH = (d.canceled / max) * plotH;
          const yOk = padTop + plotH - okH;
          const yFail = yOk - failH;
          const yCancel = yFail - cancelH;
          const showTick = data.length <= 10 || idx === 0 || idx === data.length - 1 || idx % Math.ceil(data.length / 6) === 0;
          return (
            <g key={d.date}>
              <title>{`${d.date} — Thành công: ${d.succeeded}, Thất bại: ${d.failed}, Hủy: ${d.canceled}`}</title>
              {okH > 0 && <rect x={x} y={yOk} width={barW} height={okH} fill="var(--success)" />}
              {failH > 0 && <rect x={x} y={yFail} width={barW} height={failH} fill="var(--danger)" />}
              {cancelH > 0 && <rect x={x} y={yCancel} width={barW} height={cancelH} fill="var(--text-muted)" />}
              {showTick && (
                <text x={x + barW / 2} y={height - 8} textAnchor="middle" fill="var(--text-muted)" fontSize="10">
                  {d.date.slice(5)}
                </text>
              )}
            </g>
          );
        })}
      </svg>
      <div style={{ display: 'flex', gap: 16, fontSize: 12, color: 'var(--text-muted)', marginTop: 6 }}>
        <span><span style={{ color: 'var(--success)' }}>■</span> Thành công</span>
        <span><span style={{ color: 'var(--danger)' }}>■</span> Thất bại</span>
        <span><span style={{ color: 'var(--text-muted)' }}>■</span> Đã hủy</span>
      </div>
    </div>
  );
}

export function HorizontalDistChart({
  items,
  emptyLabel = 'Chưa có dữ liệu.',
}: {
  items: { label: string; value: number; color?: string }[];
  emptyLabel?: string;
}) {
  if (items.length === 0) return <div className="notice-banner">{emptyLabel}</div>;
  const total = Math.max(1, items.reduce((acc, i) => acc + i.value, 0));
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {items.map((item) => {
        const pct = Math.round((item.value / total) * 100);
        return (
          <div key={item.label}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, marginBottom: 4 }}>
              <span style={{ fontWeight: 500, textTransform: 'capitalize' }}>{item.label}</span>
              <span className="mono" style={{ color: 'var(--text-muted)' }}>
                {item.value.toLocaleString('vi-VN')} ({pct}%)
              </span>
            </div>
            <div style={{ height: 7, borderRadius: 999, background: 'var(--bg)', overflow: 'hidden' }}>
              <div style={{ width: `${pct}%`, height: '100%', background: item.color || 'var(--accent)', borderRadius: 999 }} />
            </div>
          </div>
        );
      })}
    </div>
  );
}
