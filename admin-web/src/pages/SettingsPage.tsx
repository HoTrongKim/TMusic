import { useCallback, useEffect, useState } from 'react';
import { RotateCcw, Shield } from 'lucide-react';
import { ApiError, apiRequest } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { Modal, SkeletonRows, StatusBadge } from '../components/UiKit';

interface SettingItem {
  key: string;
  value: string;
  defaultValue: string;
  valueType: 'int' | 'bool' | 'string';
  category: string;
  description: string;
  minValue?: number | null;
  maxValue?: number | null;
  requiresConfirmation: boolean;
  updatedAt: string;
}

export function SettingsPage() {
  const { hasPermission, notify } = useAuth();
  const canEdit = hasPermission('settings.manage');
  const [items, setItems] = useState<SettingItem[]>([]);
  const [policy, setPolicy] = useState('');
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [confirmItem, setConfirmItem] = useState<SettingItem | null>(null);
  const [reason, setReason] = useState('');
  const [loading, setLoading] = useState(true);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await apiRequest<{ items: SettingItem[]; secretsPolicy: string }>('/api/admin/settings');
      setItems(res.items);
      setPolicy(res.secretsPolicy);
      const nextDrafts: Record<string, string> = {};
      for (const item of res.items) nextDrafts[item.key] = item.value;
      setDrafts(nextDrafts);
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể tải cấu hình hệ thống.', 'danger');
    } finally {
      setLoading(false);
    }
  }, [notify]);

  useEffect(() => {
    void load();
  }, [load]);

  const saveSetting = async (item: SettingItem, confirmed: boolean, customReason?: string) => {
    setBusyKey(item.key);
    try {
      await apiRequest(`/api/admin/settings/${encodeURIComponent(item.key)}`, {
        method: 'PUT',
        body: JSON.stringify({ value: drafts[item.key], confirmed, reason: customReason }),
      });
      notify(`Đã cập nhật cấu hình ${item.key} và ghi Audit Log.`, 'success');
      setConfirmItem(null);
      setReason('');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Cập nhật thất bại.', 'danger');
    } finally {
      setBusyKey(null);
    }
  };

  const resetSetting = async (item: SettingItem) => {
    setBusyKey(item.key);
    try {
      await apiRequest(`/api/admin/settings/${encodeURIComponent(item.key)}/reset`, { method: 'POST' });
      notify(`Đã khôi phục ${item.key} về mặc định (${item.defaultValue}).`, 'success');
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.detail || err.code : 'Không thể khôi phục mặc định.', 'danger');
    } finally {
      setBusyKey(null);
    }
  };

  return (
    <div className="card">
      <div className="card-header">
        <div>
          <h2 className="card-title">Cấu hình hệ thống Runtime</h2>
          <p className="card-subtitle">Chỉ hiển thị các tùy chọn an toàn có thể điều chỉnh tại runtime, có kiểm tra hợp lệ và khôi phục mặc định.</p>
        </div>
      </div>

      {policy && (
        <div className="notice-banner notice-warning" style={{ marginBottom: 16 }}>
          <Shield size={16} style={{ flexShrink: 0, marginTop: 2 }} />
          <span>{policy}</span>
        </div>
      )}

      {loading ? (
        <SkeletonRows rows={6} />
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Khóa cấu hình & Mô tả</th>
                <th>Mặc định</th>
                <th>Giá trị hiện tại</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const dirty = drafts[item.key] !== item.value;
                return (
                  <tr key={item.key}>
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <span className="mono" style={{ fontWeight: 600 }}>{item.key}</span>
                        <StatusBadge status={item.category} />
                        {item.requiresConfirmation && <span className="badge badge-warning">Ảnh hưởng lớn</span>}
                      </div>
                      <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 3 }}>{item.description}</div>
                    </td>
                    <td className="mono">{item.defaultValue || '""'}</td>
                    <td style={{ minWidth: 190 }}>
                      {item.valueType === 'bool' ? (
                        <select
                          className="select"
                          disabled={!canEdit}
                          value={drafts[item.key] ?? item.value}
                          onChange={(e) => setDrafts((d) => ({ ...d, [item.key]: e.target.value }))}
                        >
                          <option value="true">true (Bật)</option>
                          <option value="false">false (Tắt)</option>
                        </select>
                      ) : (
                        <input
                          className="input mono"
                          type={item.valueType === 'int' ? 'number' : 'text'}
                          disabled={!canEdit}
                          min={item.minValue ?? undefined}
                          max={item.maxValue ?? undefined}
                          value={drafts[item.key] ?? item.value}
                          onChange={(e) => setDrafts((d) => ({ ...d, [item.key]: e.target.value }))}
                        />
                      )}
                    </td>
                    <td>
                      {canEdit ? (
                        <div style={{ display: 'flex', gap: 6 }}>
                          <button
                            className="btn btn-sm btn-primary"
                            disabled={!dirty || busyKey === item.key}
                            onClick={() => {
                              if (item.requiresConfirmation) {
                                setConfirmItem(item);
                                setReason('');
                              } else {
                                void saveSetting(item, true);
                              }
                            }}
                          >
                            Lưu
                          </button>
                          <button
                            className="btn btn-sm"
                            disabled={item.value === item.defaultValue || busyKey === item.key}
                            onClick={() => void resetSetting(item)}
                            title="Khôi phục mặc định"
                          >
                            <RotateCcw size={13} />
                          </button>
                        </div>
                      ) : (
                        <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Chỉ đọc</span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {confirmItem && (
        <Modal title={`Xác nhận thay đổi cấu hình: ${confirmItem.key}`} onClose={() => setConfirmItem(null)}>
          <div className="notice-banner notice-warning" style={{ marginBottom: 12 }}>
            Thay đổi từ <strong className="mono">{confirmItem.value}</strong> sang <strong className="mono">{drafts[confirmItem.key]}</strong> có ảnh hưởng trực tiếp tới vận hành hệ thống.
          </div>
          <div className="form-field">
            <label>Lý do thay đổi (ghi Audit Log)</label>
            <input className="input" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="VD: Bật bảo trì nâng cấp định kỳ" />
          </div>
          <div className="modal-footer">
            <button className="btn" onClick={() => setConfirmItem(null)}>Hủy</button>
            <button className="btn btn-primary" disabled={busyKey === confirmItem.key} onClick={() => void saveSetting(confirmItem, true, reason)}>
              Xác nhận áp dụng
            </button>
          </div>
        </Modal>
      )}
    </div>
  );
}
