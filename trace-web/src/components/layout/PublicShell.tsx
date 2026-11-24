import Link from 'next/link';
import type { ReactNode } from 'react';
import { BrandMark } from '@/components/ui/BrandMark';
import { Icon } from '@/components/ui/Icon';

interface PublicShellProps {
  companyName: string;
  subtitle: string;
  logoDataUrl: string;
  footerTop: string;
  footerBottom: string;
  children: ReactNode;
}

const PUBLIC_LINKS = [
  { href: '/', label: 'Control Room' },
  { href: '/login', label: 'Login & Scan' },
  { href: '/modules/traceability/database', label: 'Database Trace' },
  { href: '/super-admin', label: 'Super Admin' },
];

export function PublicShell({
  companyName,
  subtitle,
  logoDataUrl,
  footerTop,
  footerBottom,
  children,
}: PublicShellProps) {
  return (
    <div className="public-shell">
      <header className="public-header">
        <div className="public-brand">
          <BrandMark companyName={companyName} logoDataUrl={logoDataUrl} />
          <div>
            <b>{companyName}</b>
            <small>{subtitle}</small>
          </div>
        </div>
        <nav className="public-nav" aria-label="Navigasi aplikasi">
          {PUBLIC_LINKS.map((link) => (
            <Link key={link.href} href={link.href}>
              {link.label}
            </Link>
          ))}
        </nav>
      </header>

      <main className="public-main">{children}</main>

      <footer className="public-footer">
        <span>{footerTop}</span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <Icon name="shield" size={13} />
          {footerBottom}
        </span>
      </footer>
    </div>
  );
}
