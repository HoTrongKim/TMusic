import React, { useState } from 'react';
import {
  Activity,
  BarChart3,
  Bell,
  Disc3,
  Download,
  FileText,
  HardDrive,
  LayoutDashboard,
  LogOut,
  Menu,
  Moon,
  Music2,
  Settings,
  ShieldCheck,
  Sun,
  Users,
  X,
} from 'lucide-react';
import { useAuth } from '../context/AuthContext';
import { StatusBadge } from './UiKit';

export type NavKey =
  | 'overview'
  | 'users'
  | 'songs'
  | 'albums'
  | 'downloads'
  | 'storage'
  | 'analytics'
  | 'logs'
  | 'settings'
  | 'admins';

export interface NavDefinition {
  key: NavKey;
  label: string;
  permission: string;
  icon: React.ReactNode;
}

export const NAV_ITEMS: NavDefinition[] = [
  { key: 'overview', label: 'Tổng quan', permission: 'analytics.read', icon: <LayoutDashboard size={17} /> },
  { key: 'users', label: 'Người dùng', permission: 'users.read', icon: <Users size={17} /> },
  { key: 'songs', label: 'Bài hát & Nội dung', permission: 'content.read', icon: <Music2 size={17} /> },
  { key: 'albums', label: 'Album', permission: 'albums.read', icon: <Disc3 size={17} /> },
  { key: 'downloads', label: 'Hoạt động tải xuống', permission: 'downloads.read', icon: <Download size={17} /> },
  { key: 'storage', label: 'Dung lượng lưu trữ', permission: 'storage.read', icon: <HardDrive size={17} /> },
  { key: 'analytics', label: 'Thống kê & Báo cáo', permission: 'analytics.read', icon: <BarChart3 size={17} /> },
  { key: 'logs', label: 'Nhật ký hệ thống', permission: 'audit.read', icon: <FileText size={17} /> },
  { key: 'settings', label: 'Cấu hình', permission: 'settings.read', icon: <Settings size={17} /> },
  { key: 'admins', label: 'Quản trị viên', permission: 'admins.manage', icon: <ShieldCheck size={17} /> },
];

export function Layout({
  active,
  onSelect,
  alertCount,
  children,
}: {
  active: NavKey;
  onSelect: (key: NavKey) => void;
  alertCount: number;
  children: React.ReactNode;
}) {
  const { admin, theme, toggleTheme, logout, hasPermission } = useAuth();
  const [drawerOpen, setDrawerOpen] = useState(false);

  const visibleItems = NAV_ITEMS.filter((item) => hasPermission(item.permission));
  const currentItem = visibleItems.find((i) => i.key === active) || visibleItems[0];

  const handleSelect = (key: NavKey) => {
    onSelect(key);
    setDrawerOpen(false);
  };

  return (
    <div className="admin-shell">
      <div className={`sidebar-overlay ${drawerOpen ? 'open' : ''}`} onClick={() => setDrawerOpen(false)} />

      <aside className={`sidebar ${drawerOpen ? 'open' : ''}`} aria-label="Điều hướng quản trị">
        <div className="sidebar-brand">
          <div className="brand-mark">
            <span className="brand-dot">TM</span>
            <span>TMusic Admin</span>
          </div>
          <button
            className="btn btn-icon mobile-only"
            onClick={() => setDrawerOpen(false)}
            aria-label="Đóng menu"
          >
            <X size={16} />
          </button>
        </div>

        <nav className="sidebar-nav">
          {visibleItems.map((item) => (
            <button
              key={item.key}
              className={`nav-item ${active === item.key ? 'active' : ''}`}
              onClick={() => handleSelect(item.key)}
              aria-current={active === item.key ? 'page' : undefined}
            >
              {item.icon}
              <span>{item.label}</span>
            </button>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
            <Activity size={13} style={{ color: 'var(--success)' }} />
            <span>RBAC & CSRF đang hoạt động</span>
          </div>
          <div className="mono" style={{ opacity: 0.75 }}>
            TMusic V4 Admin Console
          </div>
        </div>
      </aside>

      <div className="main-area">
        <header className="topbar">
          <div className="topbar-left">
            <button
              className="btn btn-icon mobile-only"
              onClick={() => setDrawerOpen(true)}
              aria-label="Mở menu"
            >
              <Menu size={17} />
            </button>
            <div className="breadcrumb">
              <span>TMusic Admin</span>
              <span>/</span>
              <strong>{currentItem?.label ?? 'Tổng quan'}</strong>
            </div>
          </div>

          <div className="topbar-right">
            {hasPermission('audit.read') && (
              <button
                className="btn btn-sm"
                onClick={() => handleSelect('logs')}
                title="Thông báo & sự kiện quản trị"
              >
                <Bell size={15} />
                {alertCount > 0 && <span className="badge badge-warning">{alertCount}</span>}
              </button>
            )}
            <button
              className="btn btn-icon"
              onClick={toggleTheme}
              aria-label={theme === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'}
              title={theme === 'dark' ? 'Chế độ sáng' : 'Chế độ tối'}
            >
              {theme === 'dark' ? <Sun size={15} /> : <Moon size={15} />}
            </button>
            {admin && (
              <div className="user-pill">
                <span style={{ fontWeight: 600 }}>{admin.displayName || admin.email}</span>
                <StatusBadge status={admin.roles[0] || 'Admin'} />
              </div>
            )}
            <button className="btn btn-sm" onClick={() => void logout()} title="Đăng xuất">
              <LogOut size={14} />
              <span>Đăng xuất</span>
            </button>
          </div>
        </header>

        <main className="content-wrap">{children}</main>
      </div>
    </div>
  );
}
