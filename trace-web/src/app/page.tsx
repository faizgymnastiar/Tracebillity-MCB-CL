import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AppShell } from '@/components/layout/AppShell';
import { ProductionDesk } from '@/components/traceability/ProductionDesk';
import { AdminProductionDashboard } from '@/components/traceability/AdminProductionDashboard';
import { PageHeader, Panel } from '@/components/ui/SharedUi';
import { Icon, type IconName } from '@/components/ui/Icon';
import { NAVIGATION_GROUPS } from '@/lib/navigation';
import { getDailySummary, getHealth, getPublicConfig } from '@/server/trace-backend';
import { formatDateDisplay, formatNumber } from '@/lib/formatters';
import { PERMISSIONS, getTraceSession } from '@/server/session';

export const dynamic = 'force-dynamic';

export default async function HomePage({ searchParams }: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const { profile, remote } = await getTraceSession();
  if (!profile.authenticated || remote?.mustChangePassword) redirect('/login');

  const canReadHistory = profile.permissions.includes(PERMISSIONS.history);
  const showAdminReport = canReadHistory && profile.permissions.includes(PERMISSIONS.admin);
  const [config, health, summary] = await Promise.all([
    getPublicConfig(),
    getHealth(),
    canReadHistory && !showAdminReport ? getDailySummary() : null,
  ]);
  const modules = NAVIGATION_GROUPS.flatMap((group) => group.items)
    .filter((item) => item.href !== '/' && profile.permissions.includes(item.requiredPermission));

  return (
    <AppShell profile={profile} companyName={config.companyName} subtitle={config.subtitle}
      logoDataUrl={config.logoDataUrl} health={health}>
      <div className="control-room">
        <PageHeader eyebrow="Traceability" title={showAdminReport ? 'Dashboard Produksi' : config.heroTitle}
          description={`Halo, ${profile.fullName}. ${profile.roles.join(' · ')}`} />
        {showAdminReport && <AdminProductionDashboard query={await searchParams} />}
        {profile.permissions.includes(PERMISSIONS.write) && <ProductionDesk />}
        <Panel title="Modul Anda">
          {modules.length ? (
            <ul className="rbac-modules">
              {modules.map((item) => (
                <li key={item.slug}>
                  <Link href={item.href}>
                    <span className="control-room-module-icon" aria-hidden="true">
                      <Icon name={(item.icon as IconName | undefined) ?? 'home'} size={19} />
                    </span>
                    <strong>{item.label}</strong>
                    <span className="control-room-module-description">{item.description}</span>
                    <span className="control-room-module-arrow" aria-hidden="true"><Icon name="arrow-right" size={16} /></span>
                  </Link>
                </li>
              ))}
            </ul>
          ) : <p>Belum ada izin modul. Hubungi Admin Akun.</p>}
        </Panel>
        {summary && <Panel title="Rekap produksi harian">
          <div className="table-wrap">
            <table className="erp-adjustable-grid">
              <thead><tr><th>Tanggal</th><th>Total</th><th>Direct OK</th><th>After Rework</th><th>NG</th><th>Inner</th><th>Outer</th></tr></thead>
              <tbody>{summary.items.slice(0, 4).map((row) => (
                <tr key={row.isoDate}>
                  <td>{formatDateDisplay(row.isoDate)}</td>
                  <td>{formatNumber(row.total)}</td><td>{formatNumber(row.directOk)}</td>
                  <td>{formatNumber(row.afterRework)}</td><td>{formatNumber(row.ngCount)}</td>
                  <td>{formatNumber(row.innerBoxes)}</td><td>{formatNumber(row.outerBoxes)}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        </Panel>}
      </div>
    </AppShell>
  );
}
