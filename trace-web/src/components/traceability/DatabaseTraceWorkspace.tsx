'use client';

import { scanInputProps } from '@/lib/scanInput';
import Link from 'next/link';
import { useCallback, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  DailySummaryResponse,
  DataTableColumn,
  TraceabilityRecord,
  TraceRecordListResponse,
  TraceStatusFilter,
} from '@/types';
import { cn } from '@/lib/cn';
import { formatDateDisplay, formatNumber, formatPercent } from '@/lib/formatters';
import { DataTable } from '@/components/ui/DataTable';
import { Icon } from '@/components/ui/Icon';
import { EmptyState, PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';

interface DatabaseTraceWorkspaceProps {
  initialData: TraceRecordListResponse;
  initialSummary: DailySummaryResponse;
  initialSearch: string;
  initialDate: string;
  initialStatus: TraceStatusFilter;
  headerTitle: string;
  searchTitle: string;
  tableTitle: string;
}

const STATUS_OPTIONS: Array<{ value: TraceStatusFilter; label: string }> = [
  { value: 'ALL', label: 'Semua status' },
  { value: 'OK', label: 'OK / Lulus' },
  { value: 'NG', label: 'NG / Karantina' },
  { value: 'REWORK', label: 'After rework' },
];

function statusTone(category: TraceabilityRecord['category']): 'ok' | 'ng' | 'rework' {
  if (category === 'NG') return 'ng';
  if (category === 'REWORK') return 'rework';
  return 'ok';
}

function detailHref(id: string): string {
  return `/modules/traceability/database/${encodeURIComponent(id)}`;
}

export function DatabaseTraceWorkspace({
  initialData,
  initialSummary,
  initialSearch,
  initialDate,
  initialStatus,
  headerTitle,
  searchTitle,
  tableTitle,
}: DatabaseTraceWorkspaceProps) {
  const [data, setData] = useState(initialData);
  const [summary, setSummary] = useState(initialSummary);
  const [search, setSearch] = useState(initialSearch);
  const [date, setDate] = useState(initialDate);
  const [status, setStatus] = useState<TraceStatusFilter>(initialStatus);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(
    async (next: { search: string; date: string; status: TraceStatusFilter }): Promise<void> => {
      setBusy(true);
      setError('');
      try {
        const query = new URLSearchParams({
          search: next.search,
          date: next.date,
          status: next.status,
          page: '1',
          pageSize: '250',
        });
        const [recordsResponse, summaryResponse] = await Promise.all([
          fetch(`/api/v1/traceability/records?${query.toString()}`, { cache: 'no-store' }),
          fetch('/api/v1/traceability/daily-summary', { cache: 'no-store' }),
        ]);
        setData((await recordsResponse.json()) as TraceRecordListResponse);
        setSummary((await summaryResponse.json()) as DailySummaryResponse);
      } catch {
        setError('Data traceability gagal dimuat. Periksa koneksi API lalu ulangi.');
      } finally {
        setBusy(false);
      }
    },
    [],
  );

  const submitFilters = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    await load({ search, date, status });
  };

  const resetFilters = async (): Promise<void> => {
    setSearch('');
    setDate('');
    setStatus('ALL');
    await load({ search: '', date: '', status: 'ALL' });
  };

  const applyBox = async (boxId: string): Promise<void> => {
    setSearch(boxId);
    await load({ search: boxId, date, status });
  };

  const destinationSummary = useMemo(() => {
    const buckets = new Map<string, { total: number; ok: number; ng: number }>();
    for (const record of data.items) {
      const key = record.destination || 'N/A';
      const current = buckets.get(key) ?? { total: 0, ok: 0, ng: 0 };
      current.total += 1;
      if (record.category === 'OK') current.ok += 1;
      if (record.category === 'NG') current.ng += 1;
      buckets.set(key, current);
    }
    return [...buckets.entries()].sort((left, right) => right[1].total - left[1].total).slice(0, 6);
  }, [data.items]);

  const exportHref = `/api/v1/traceability/export?${new URLSearchParams({
    search,
    date,
    status,
  }).toString()}`;

  const columns: DataTableColumn<TraceabilityRecord>[] = [
    {
      key: 'no',
      label: 'No',
      align: 'center',
      defaultWidth: 56,
      render: (_row, index) => <span className="mono">{index + 1}</span>,
    },
    {
      key: 'isoDate',
      label: 'Tanggal',
      defaultWidth: 108,
      render: (row) => <span className="mono">{formatDateDisplay(row.isoDate)}</span>,
    },
    { key: 'waktu', label: 'Waktu', defaultWidth: 96 },
    {
      key: 'sn',
      label: 'Serial Number',
      defaultWidth: 168,
      render: (row) => (
        <Link href={detailHref(row.id)} className="mono" style={{ fontWeight: 600 }}>
          {row.sn}
        </Link>
      ),
    },
    {
      key: 'status',
      label: 'Status QC',
      defaultWidth: 128,
      render: (row) => <StatusBadge tone={statusTone(row.category)}>{row.status}</StatusBadge>,
    },
    { key: 'station', label: 'Station NG', defaultWidth: 150, optional: true },
    { key: 'sku', label: 'Model / SKU', defaultWidth: 150 },
    { key: 'wo', label: 'Work Order', defaultWidth: 140 },
    { key: 'lot', label: 'Material Lot', defaultWidth: 150 },
    {
      key: 'innerBox',
      label: 'Inner Box',
      defaultWidth: 150,
      render: (row) =>
        row.innerBox && !row.innerBox.startsWith('IN-PROGRESS') && row.innerBox !== 'KARANTINA QC' &&
        row.innerBox !== 'REWORK GATE' ? (
          <Link href={detailHref(row.innerBox)} className="mono">
            {row.innerBox}
          </Link>
        ) : (
          <span className="muted">{row.innerBox}</span>
        ),
    },
    {
      key: 'outerBox',
      label: 'Outer Box',
      defaultWidth: 150,
      render: (row) =>
        row.outerBox && row.outerBox !== 'PENDING OUTER' && row.outerBox !== '-' && row.outerBox !== 'N/A' ? (
          <Link href={detailHref(row.outerBox)} className="mono">
            {row.outerBox}
          </Link>
        ) : (
          <span className="muted">{row.outerBox}</span>
        ),
    },
    { key: 'destination', label: 'Tujuan', defaultWidth: 160 },
    { key: 'reworkTicket', label: 'Tiket Rework', defaultWidth: 140, optional: true },
    { key: 'line', label: 'Line / Mesin', defaultWidth: 140, optional: true },
    { key: 'shift', label: 'Shift', defaultWidth: 92, optional: true },
    { key: 'operator', label: 'Operator', defaultWidth: 130 },
    { key: 'keterangan', label: 'Keterangan', defaultWidth: 260 },
  ];

  const todayRow = summary.items.find((row) => row.isoDate === data.lastSyncAt?.slice(0, 10));

  return (
    <>
      <PageHeader
        eyebrow="TRACEABILITY • DATABASE TRACE"
        title={headerTitle}
        description={`${formatNumber(data.total)} baris traceability siap diaudit. Cari serial number, inner box, atau outer box.`}
        actions={
          <div className="panel-actions">
            <StatusBadge tone="ok">OK {formatNumber(data.counts.ok)}</StatusBadge>
            <StatusBadge tone="rework">RWK {formatNumber(data.counts.rework)}</StatusBadge>
            <StatusBadge tone="ng">NG {formatNumber(data.counts.ng)}</StatusBadge>
          </div>
        }
      />

      <section className="filters">
        <form onSubmit={submitFilters} style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <label className="grow" style={{ flex: '1 1 320px' }}>
            {searchTitle}
            <input {...scanInputProps}
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="SN / Inner Box / Outer Box / Work Order / Lot"
              autoComplete="off"
            />
          </label>
          <label style={{ flex: '0 1 170px' }}>
            Tanggal produksi
            <input type="date" value={date} onChange={(event) => setDate(event.target.value)} />
          </label>
          <label style={{ flex: '0 1 170px' }}>
            Status QC
            <select value={status} onChange={(event) => setStatus(event.target.value as TraceStatusFilter)}>
              {STATUS_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" className="btn-primary" disabled={busy}>
            <Icon name="search" size={14} /> Cari
          </button>
          <button type="button" onClick={() => void resetFilters()} disabled={busy}>
            <Icon name="refresh" size={14} /> Reset
          </button>
          <a href={exportHref} className="hero-button ghost" style={{ borderColor: '#cbd5e1', color: '#161b41', padding: '8px 12px' }}>
            <Icon name="download" size={14} /> Export CSV
          </a>
        </form>
        {error ? <p className="helper" style={{ color: '#a30f2d', marginTop: 8 }}>{error}</p> : null}
        {data.recentBoxes.length > 0 ? (
          <div className="chip-row" style={{ marginTop: 12 }}>
            <span className="helper">Box terbaru:</span>
            {data.recentBoxes.map((box) => (
              <button
                key={`${box.type}-${box.id}`}
                type="button"
                className={cn('chip', box.type === 'OUTER' ? 'rw' : 'ok')}
                onClick={() => void applyBox(box.id)}
              >
                {box.type} <b className="mono">{box.id}</b>
              </button>
            ))}
          </div>
        ) : null}
        {data.focus ? (
          <div className="chip-row" style={{ marginTop: 10 }}>
            <span className="helper">Fokus pencarian:</span>
            <Link href={detailHref(data.focus.id)} className="chip ok">
              {data.focus.kind.toUpperCase()} <b className="mono">{data.focus.title}</b>
            </Link>
          </div>
        ) : null}
      </section>

      <DataTable
        columns={columns}
        rows={data.items}
        rowKey={(row) => row.id}
        storageKey="database-trace"
        title={tableTitle}
        helper={`${formatNumber(data.items.length)} dari ${formatNumber(data.total)} baris ditampilkan. `
          + 'Klik header untuk mengurutkan, klik kanan untuk menyembunyikan kolom.'}
        pageSizeOptions={[25, 50, 100, 250]}
        initialPageSize={50}
        toolbar={<span className="helper">Sinkronisasi {summary.lastSyncAt.slice(11, 19)} UTC</span>}
      />

      {data.items.length === 0 ? <EmptyState>Tidak ada baris yang cocok dengan filter saat ini.</EmptyState> : null}

      <section className="split-grid">
        <Panel title="Rekap produksi harian" actions={<span className="helper">{summary.totalDays} hari</span>}>
          <div className="table-wrap" style={{ maxHeight: 'none' }}>
            <table className="erp-adjustable-grid">
              <thead>
                <tr>
                  <th>Tanggal</th>
                  <th>Hari</th>
                  <th style={{ textAlign: 'right' }}>Total</th>
                  <th style={{ textAlign: 'right' }}>Direct OK</th>
                  <th style={{ textAlign: 'right' }}>After RWK</th>
                  <th style={{ textAlign: 'right' }}>NG</th>
                  <th style={{ textAlign: 'right' }}>Inner</th>
                  <th style={{ textAlign: 'right' }}>Outer</th>
                  <th style={{ textAlign: 'right' }}>FPY</th>
                </tr>
              </thead>
              <tbody>
                {summary.items.map((row) => (
                  <tr key={row.isoDate}>
                    <td className="mono">{formatDateDisplay(row.isoDate)}</td>
                    <td>{row.dayName}</td>
                    <td style={{ textAlign: 'right' }}>{formatNumber(row.total)}</td>
                    <td style={{ textAlign: 'right', color: '#0b6b46' }}>{formatNumber(row.directOk)}</td>
                    <td style={{ textAlign: 'right', color: '#a34f00' }}>{formatNumber(row.afterRework)}</td>
                    <td style={{ textAlign: 'right', color: '#a30f2d' }}>{formatNumber(row.ngCount)}</td>
                    <td style={{ textAlign: 'right' }}>{formatNumber(row.innerBoxes)}</td>
                    <td style={{ textAlign: 'right' }}>{formatNumber(row.outerBoxes)}</td>
                    <td style={{ textAlign: 'right' }}>{formatPercent(row.directOk, row.total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {todayRow ? (
            <p className="helper" style={{ marginTop: 10, marginBottom: 0 }}>
              Hari aktif {todayRow.dayName}: {formatNumber(todayRow.total)} unit, {formatNumber(todayRow.innerBoxes)} inner box,{' '}
              {formatNumber(todayRow.outerBoxes)} outer box.
            </p>
          ) : null}
        </Panel>

        <Panel title="Distribusi tujuan pengiriman" actions={<span className="helper">Top 6</span>}>
          {destinationSummary.length === 0 ? (
            <EmptyState>Belum ada data tujuan pada hasil filter ini.</EmptyState>
          ) : (
            <div className="list">
              {destinationSummary.map(([destination, stats]) => (
                <div className="list-row" key={destination}>
                  <div>
                    <b>{destination}</b>
                    <p>
                      {formatNumber(stats.total)} baris • OK {formatNumber(stats.ok)} • NG {formatNumber(stats.ng)}
                    </p>
                  </div>
                  <div className="list-tail">{formatPercent(stats.ok, stats.total)} OK</div>
                </div>
              ))}
            </div>
          )}
        </Panel>
      </section>
    </>
  );
}
