import { notFound, redirect } from 'next/navigation';
import type { ReactNode } from 'react';
import type { ScanMode } from '@/types';
import { AppShell } from '@/components/layout/AppShell';
import { ApiOfflineNotice } from '@/components/ui/ApiOfflineNotice';
import { PackingHistory, PackingWorkspace } from '@/components/traceability/PackingWorkspace';
import { ExportStudioWorkspace } from '@/components/traceability/ExportStudioWorkspace';
import { LabelStockWorkspace } from '@/components/traceability/LabelStockWorkspace';
import { NgReworkWorkspace } from '@/components/traceability/NgReworkWorkspace';
import { ScanWorkspace } from '@/components/traceability/ScanWorkspace';
import { PERMISSIONS, getTraceSession, readOperatorContext } from '@/server/session';
import {
  TRACE_API_ORIGIN,
  getDailySummary,
  getHealth,
  getLabelSummary,
  getNgStations,
  getNgSummary,
  getPublicConfig,
  getScanState,
  isTraceApiReachable,
  listRecords,
} from '@/server/trace-backend';

export const dynamic = 'force-dynamic';

interface ModulePageProps {
  params: Promise<{ group: string; slug: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function firstValue(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

function parseMode(value: string): ScanMode {
  const normalized = value.toUpperCase();
  return normalized === 'NG' || normalized === 'REWORK' ? normalized : 'OK';
}

export default async function ModulePage({ params, searchParams }: ModulePageProps) {
  const { group, slug } = await params;
  const query = await searchParams;

  if (group !== 'traceability') notFound();
  if (!['scan', 'database', 'reports', 'labels', 'quarantine', 'packing'].includes(slug)) {
    notFound();
  }

  const [session, config, health, context] = await Promise.all([
    getTraceSession(),
    getPublicConfig(),
    getHealth(),
    readOperatorContext(),
  ]);
  const { profile, remote } = session;
  if (!profile.authenticated || remote?.mustChangePassword) redirect('/login');

  const shell = (children: ReactNode) => (
    <AppShell
      profile={profile}
      companyName={config.companyName}
      subtitle={config.subtitle}
      logoDataUrl={config.logoDataUrl}
      health={health}
    >
      {children}
    </AppShell>
  );

  if (slug === 'scan' && !remote) {
    if (!profile.permissions.includes(PERMISSIONS.write)) redirect('/');
    if (!(await isTraceApiReachable())) return shell(<ApiOfflineNotice origin={TRACE_API_ORIGIN} />);
    const snapshot = await getScanState(context);
    return shell(<ScanWorkspace initialSnapshot={snapshot} initialMode={parseMode(firstValue(query.mode))} />);
  }

  if (slug === 'packing') {
    if (!profile.authenticated) redirect('/login');
    const kinds = (['inner', 'outer'] as const).filter((kind) =>
      session.remote?.permissions.includes(`scan.${kind}`));
    if (kinds.length === 0) redirect('/');
    return shell(<PackingWorkspace production={session.production} kinds={kinds} />);
  }

  // Fase 3: layar karantina & rework — hak tulis scan/NG; tab mengikuti hak backend.
  if (slug === 'quarantine' || slug === 'scan') {
    const permission = slug === 'scan' ? 'scan.unit' : 'ng.record';
    if (!profile.permissions.includes(permission)) redirect('/');
    if (!(await isTraceApiReachable())) return shell(<ApiOfflineNotice origin={TRACE_API_ORIGIN} />);
    const [summary, stations] = await Promise.all([
      getNgSummary(context),
      getNgStations(context),
    ]);
    return shell(
      <NgReworkWorkspace
        initialSummary={summary}
        initialStations={stations}
        contextVersion={session.production?.contextVersion ?? null}
        production={session.production}
        reworkActions={config.reworks}
        initialTab={slug === 'scan' ? 'ok' : 'ng'}
      />,
    );
  }

  // Fase 2: layar stok label — butuh hak generate/cetak atau hak baca riwayat.
  if (slug === 'labels') {
    if (!profile.authenticated) redirect('/login');
    const canManage = profile.permissions.includes(PERMISSIONS.label);
    if (!canManage && !profile.permissions.includes(PERMISSIONS.history)) redirect('/');
    if (!(await isTraceApiReachable())) return shell(<ApiOfflineNotice origin={TRACE_API_ORIGIN} />);
    const stock = await getLabelSummary(context);
    return shell(<LabelStockWorkspace initialSummary={stock} canManage={canManage} />);
  }

  // Fase 1: seluruh layar baca-data menuntut sesi login + hak riwayat dari backend.
  if (!profile.authenticated) redirect('/login');
  if (!profile.permissions.includes(PERMISSIONS.history)) redirect('/');

  if (slug === 'database') {
    if (!(await isTraceApiReachable())) return shell(<ApiOfflineNotice origin={TRACE_API_ORIGIN} />);
    return shell(<PackingHistory initialSearch={firstValue(query.search)} />);
  }

  if (!(await isTraceApiReachable())) return shell(<ApiOfflineNotice origin={TRACE_API_ORIGIN} />);

  const [data, summary] = await Promise.all([
    listRecords({ search: '', date: firstValue(query.date), status: 'ALL', page: 1, pageSize: 250 }, context),
    getDailySummary(context),
  ]);
  return shell(
    <ExportStudioWorkspace
      initialData={data}
      initialSummary={summary}
      title={config.uiReportTitle}
      companyName={config.companyName}
      templates={config.exportTemplates}
    />,
  );
}
