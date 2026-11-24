import { SuperAdminWorkspace } from '@/components/admin/SuperAdminWorkspace';
import { AccountsPanel } from '@/components/admin/AccountsPanel';
import { redirect } from 'next/navigation';
import { AppShell } from '@/components/layout/AppShell';
import { ApiOfflineNotice } from '@/components/ui/ApiOfflineNotice';
import { getTraceSession } from '@/server/session';
import { TRACE_API_ORIGIN, getHealth, getPublicConfig, isTraceApiReachable } from '@/server/trace-backend';

export const dynamic = 'force-dynamic';

export default async function SuperAdminPage() {
  const [session, config, health, online] = await Promise.all([
    getTraceSession(),
    getPublicConfig(),
    getHealth(),
    isTraceApiReachable(),
  ]);
  const { profile, remote } = session;
  if (!remote || remote.mustChangePassword) redirect('/login');
  const admin = remote.permissions.includes('admin.master');
  const canManageAccounts = remote.permissions.includes('admin.accounts');
  if (!admin && !canManageAccounts) redirect('/');

  return (
    <AppShell
      profile={profile}
      companyName={config.companyName}
      subtitle={config.subtitle}
      logoDataUrl={config.logoDataUrl}
      health={health}
    >
      {online ? (
        admin
          ? <SuperAdminWorkspace initialConfig={config} admin={admin} canManageAccounts={canManageAccounts} profileName={profile.fullName} />
          : <AccountsPanel />
      ) : (
        <ApiOfflineNotice origin={TRACE_API_ORIGIN} />
      )}
    </AppShell>
  );
}
