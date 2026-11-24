import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import { AppShell } from '@/components/layout/AppShell';
import { TraceDetailView } from '@/components/traceability/TraceDetailView';
import { ApiOfflineNotice } from '@/components/ui/ApiOfflineNotice';
import { EmptyState, Panel } from '@/components/ui/SharedUi';
import { getTraceSession, PERMISSIONS, readOperatorContext } from '@/server/session';
import {
  TRACE_API_ORIGIN,
  getHealth,
  getPublicConfig,
  getTraceDetail,
  isTraceApiReachable,
} from '@/server/trace-backend';

export const dynamic = 'force-dynamic';

interface DetailPageProps {
  params: Promise<{ group: string; slug: string; id: string }>;
}

export default async function TraceDetailPage({ params }: DetailPageProps) {
  const { group, slug, id } = await params;
  if (group !== 'traceability' || slug !== 'database') notFound();

  const [session, config, health, context] = await Promise.all([
    getTraceSession(),
    getPublicConfig(),
    getHealth(),
    readOperatorContext(),
  ]);
  const { profile, remote } = session;
  if (!profile.authenticated || remote?.mustChangePassword) redirect('/login');
  if (!profile.permissions.includes(PERMISSIONS.history)) redirect('/');

  const online = await isTraceApiReachable();
  let detail = null;
  if (online) {
    try {
      detail = await getTraceDetail(id, context);
    } catch {
      // 403 (tanpa hak history.read) / 500 diperlakukan sebagai "tidak dapat ditampilkan".
      detail = null;
    }
  }

  return (
    <AppShell
      profile={profile}
      companyName={config.companyName}
      subtitle={config.subtitle}
      logoDataUrl={config.logoDataUrl}
      health={health}
    >
      {detail ? (
        <TraceDetailView detail={detail} />
      ) : !online ? (
        <ApiOfflineNotice origin={TRACE_API_ORIGIN} />
      ) : (
        <Panel title="Entitas tidak ditemukan">
          <EmptyState>
            Tidak ada serial number, inner box, atau outer box dengan kode <code>{id}</code>. Periksa kembali hasil scan
            atau kembali ke database traceability.
          </EmptyState>
          <div className="form-actions" style={{ justifyContent: 'center' }}>
            <Link href="/modules/traceability/database" className="hero-button ghost"
              style={{ borderColor: '#cbd5e1', color: '#161b41', padding: '8px 12px' }}>
              Kembali ke database
            </Link>
          </div>
        </Panel>
      )}
    </AppShell>
  );
}
