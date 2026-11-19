import { useMemo, useState } from 'react';
import { AuthProvider, useAuth } from './context/AuthContext';
import { Layout, NAV_ITEMS, NavKey } from './components/Layout';
import { ToastStack } from './components/UiKit';
import { LoginPage } from './pages/LoginPage';
import { AcceptInvitePage } from './pages/AcceptInvitePage';
import { OverviewPage } from './pages/OverviewPage';
import { UsersPage } from './pages/UsersPage';
import { SongsPage } from './pages/SongsPage';
import { AlbumsPage } from './pages/AlbumsPage';
import { DownloadsPage } from './pages/DownloadsPage';
import { StoragePage } from './pages/StoragePage';
import { AnalyticsPage } from './pages/AnalyticsPage';
import { LogsPage } from './pages/LogsPage';
import { SettingsPage } from './pages/SettingsPage';
import { AdminsPage } from './pages/AdminsPage';

function AdminRouter() {
  const { admin, loading, hasPermission, toasts } = useAuth();
  const [active, setActive] = useState<NavKey>('overview');
  const [alertCount, setAlertCount] = useState(0);

  const inviteToken = useMemo(() => {
    if (typeof window === 'undefined') return null;
    if (!window.location.pathname.startsWith('/accept-invite')) return null;
    return new URLSearchParams(window.location.search).get('token');
  }, []);

  if (inviteToken) {
    return (
      <>
        <AcceptInvitePage
          token={inviteToken}
          onDone={() => {
            window.history.replaceState({}, '', '/');
            window.location.reload();
          }}
        />
        <ToastStack toasts={toasts} />
      </>
    );
  }

  if (loading) {
    return (
      <div className="auth-page">
        <div className="auth-card" style={{ textAlign: 'center' }}>
          <div className="skeleton" style={{ height: 24, width: 160, margin: '0 auto 12px' }} />
          <div style={{ color: 'var(--text-muted)' }}>Đang xác minh phiên TMusic Admin...</div>
        </div>
      </div>
    );
  }

  if (!admin) {
    return (
      <>
        <LoginPage />
        <ToastStack toasts={toasts} />
      </>
    );
  }

  const permittedKeys = NAV_ITEMS.filter((n) => hasPermission(n.permission)).map((n) => n.key);
  const currentKey: NavKey = permittedKeys.includes(active) ? active : permittedKeys[0] || 'overview';

  const renderSection = () => {
    switch (currentKey) {
      case 'overview':
        return <OverviewPage onAlertsUpdate={setAlertCount} />;
      case 'users':
        return <UsersPage />;
      case 'songs':
        return <SongsPage />;
      case 'albums':
        return <AlbumsPage />;
      case 'downloads':
        return <DownloadsPage />;
      case 'storage':
        return <StoragePage />;
      case 'analytics':
        return <AnalyticsPage />;
      case 'logs':
        return <LogsPage />;
      case 'settings':
        return <SettingsPage />;
      case 'admins':
        return <AdminsPage />;
      default:
        return <OverviewPage onAlertsUpdate={setAlertCount} />;
    }
  };

  return (
    <>
      <Layout active={currentKey} onSelect={setActive} alertCount={alertCount}>
        {renderSection()}
      </Layout>
      <ToastStack toasts={toasts} />
    </>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <AdminRouter />
    </AuthProvider>
  );
}
