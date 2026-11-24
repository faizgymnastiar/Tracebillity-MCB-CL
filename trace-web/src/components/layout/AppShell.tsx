'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useState } from 'react';
import type { ReactNode } from 'react';
import type { AuthProfile, HealthStatus } from '@/types';
import { NAVIGATION_GROUPS } from '@/lib/navigation';
import { cn } from '@/lib/cn';
import { BrandMark } from '@/components/ui/BrandMark';
import { Icon, type IconName } from '@/components/ui/Icon';
import { LiveClock } from '@/components/ui/LiveClock';

interface AppShellProps {
  children: ReactNode;
  profile: AuthProfile;
  companyName: string;
  subtitle: string;
  logoDataUrl: string;
  health: HealthStatus | null;
}

type MenuMode = 'auto' | 'open' | 'closed';

export function AppShell({ children, profile, companyName, subtitle, logoDataUrl, health }: AppShellProps) {
  const pathname = usePathname();
  const router = useRouter();
  const [menuMode, setMenuMode] = useState<MenuMode>('auto');

  const isActive = (href: string): boolean =>
    href === '/' ? pathname === '/' : pathname === href || pathname.startsWith(`${href}/`);

  const handleLogout = async () => {
    await fetch('/api/v1/auth/logout', { method: 'POST' });
    router.push('/login');
    router.refresh();
  };

  const groups = NAVIGATION_GROUPS.map((group) => ({
    ...group,
    items: group.items.filter((item) => profile.permissions.includes(item.requiredPermission)),
  })).filter((group) => group.items.length > 0);

  return (
    <div
      className={cn(
        'ltn-shell-root',
        menuMode === 'open' && 'menu-open',
        menuMode === 'closed' && 'menu-closed',
      )}
    >
      <aside id="sidebar">
        <div className="brand">
          <BrandMark companyName={companyName} logoDataUrl={logoDataUrl} size={40} />
          <div className="brand-text">
            <b>{companyName}</b>
            <small>{subtitle}</small>
          </div>
          <button
            id="closeMenu"
            type="button"
            aria-label="Tutup menu"
            onClick={() => setMenuMode('closed')}
          >
            <Icon name="close" size={16} />
          </button>
        </div>

        <div className="nav-label">MODUL APLIKASI</div>

        <nav id="modules">
          {groups.map((group) => (
            <details key={group.id} className="erp-menu-group" open={group.defaultOpen ?? true}>
              <summary>{group.label}</summary>
              {group.items.map((item) => (
                <Link
                  key={item.slug}
                  href={item.href}
                  className={cn('erp-child-link', isActive(item.href) && 'active')}
                  aria-current={isActive(item.href) ? 'page' : undefined}
                  title={item.description}
                >
                  <Icon name={item.icon as IconName} size={15} />
                  <span>{item.label}</span>
                </Link>
              ))}
            </details>
          ))}
        </nav>

        <div id="sidebarUserBar">
          <span>
            <b>{profile.fullName}</b>
            <br />
            <span style={{ fontSize: 10.5 }}>{profile.roles.join(' • ')}</span>
          </span>
          <button type="button" className="topbar-icon" onClick={handleLogout} aria-label="Keluar sesi operator">
            <Icon name="logout" size={14} />
          </button>
        </div>
      </aside>

      <div className="ltn-main-area">
        <header className="ltn-topbar">
          <div className="breadcrumb">
            <button
              type="button"
              className="topbar-icon"
              aria-label="Buka menu navigasi"
              onClick={() => setMenuMode((current) => (current === 'open' ? 'closed' : 'open'))}
            >
              <Icon name="menu" size={16} />
            </button>
            <span className="shell-company">
              <Icon name="shield" size={15} />
              Traceability MCB-CL
            </span>
            <div className="topbar-title">
              <b>End-of-Line Quality Gate</b>
              <small>MANUFACTURING &amp; ELECTRICAL ENGINEERING</small>
            </div>
          </div>
          <div className="header-right">
            <span className={cn('shell-health', health?.status !== 'ok' && 'warn')}>
              <i />
              {health?.status === 'ok' ? 'API LIVE' : 'API OFFLINE'}
            </span>
            <LiveClock compact />
            <span className="shell-user">
              <Icon name="admin" size={14} />
              <b>{profile.username}</b>
            </span>
          </div>
        </header>

        <main id="erpWorkspace">{children}</main>
      </div>
    </div>
  );
}
