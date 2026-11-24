'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import type {
  DailySummaryResponse,
  ExportTemplate,
  TraceabilityRecord,
  TraceRecordListResponse,
  TraceStatusFilter,
} from '@/types';
import { ALL_EXPORT_COLUMNS } from '@/lib/config';
import { cn } from '@/lib/cn';
import { getFullTimestamp } from '@/lib/datetime';
import { formatDateDisplay, formatNumber, formatPercent } from '@/lib/formatters';
import { EmptyState, PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';

interface ExportStudioWorkspaceProps {
  initialData: TraceRecordListResponse;
  initialSummary: DailySummaryResponse;
  title: string;
  companyName: string;
  templates: ExportTemplate[];
}

const STATUS_OPTIONS: Array<{ value: TraceStatusFilter; label: string }> = [
  { value: 'ALL', label: 'Semua status' },
  { value: 'OK', label: 'OK / Lulus' },
  { value: 'NG', label: 'NG / Karantina' },
  { value: 'REWORK', label: 'After rework' },
];

const PREVIEW_LIMIT = 120;

function cellText(record: TraceabilityRecord, key: string): string {
  const value = (record as unknown as Record<string, unknown>)[key];
  if (value === null || value === undefined) return '—';
  const text = String(value).trim();
  return text === '' ? '—' : text;
}

export function ExportStudioWorkspace({
  initialData,
  initialSummary,
  title,
  companyName,
  templates,
}: ExportStudioWorkspaceProps) {
  const [data, setData] = useState(initialData);
  const [summary, setSummary] = useState(initialSummary);
  const [date, setDate] = useState(initialData.items[0]?.isoDate ?? '');
  const [status, setStatus] = useState<TraceStatusFilter>('ALL');
  const [columns, setColumns] = useState<string[]>(ALL_EXPORT_COLUMNS.filter((column) => column.defaultOn).map((column) => column.key));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [printedAt, setPrintedAt] = useState('');

  useEffect(() => {
    setPrintedAt(getFullTimestamp());
  }, []);

  const activeColumns = useMemo(
    () => ALL_EXPORT_COLUMNS.filter((column) => column.key !== 'no' && columns.includes(column.key)),
    [columns],
  );

  const load = useCallback(
    async (next: { date: string; status: TraceStatusFilter }): Promise<void> => {
      setBusy(true);
      setError('');
      try {
        const query = new URLSearchParams({
          search: '',
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
        setError('Data laporan gagal dimuat. Periksa koneksi API lalu ulangi.');
      } finally {
        setBusy(false);
      }
    },
    [],
  );

  const toggleColumn = (key: string): void => {
    setColumns((current) => (current.includes(key) ? current.filter((item) => item !== key) : [...current, key]));
  };

  const selectAll = (): void => setColumns(ALL_EXPORT_COLUMNS.map((column) => column.key));
  const selectDefault = (): void =>
    setColumns(ALL_EXPORT_COLUMNS.filter((column) => column.defaultOn).map((column) => column.key));

  const exportHref = `/api/v1/traceability/export?${new URLSearchParams({
    search: '',
    date,
    status,
    columns: activeColumns.map((column) => column.key).join(','),
  }).toString()}`;

  const totals = useMemo(() => {
    const total = data.items.length;
    const directOk = data.items.filter((record) => record.category === 'OK').length;
    const afterRework = data.items.filter((record) => record.category === 'REWORK').length;
    const ng = data.items.filter((record) => record.category === 'NG').length;
    return { total, directOk, afterRework, ng };
  }, [data.items]);

  return (
    <>
      <PageHeader
        eyebrow="TRACEABILITY • EXPORT STUDIO"
        title={title}
        description={`Laporan siap cetak dan ekspor CSV untuk ${formatNumber(data.total)} baris traceability.`}
        actions={
          <div className="panel-actions no-print">
            <StatusBadge tone="neutral">{activeColumns.length} kolom aktif</StatusBadge>
            <StatusBadge tone="ok">Siap audit</StatusBadge>
          </div>
        }
      />

      <section className="filters no-print">
        <div className="report-toolbar">
          <label>
            Tanggal produksi
            <input type="date" value={date} onChange={(event) => setDate(event.target.value)} />
          </label>
          <label>
            Status QC
            <select value={status} onChange={(event) => setStatus(event.target.value as TraceStatusFilter)}>
              {STATUS_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className="btn-primary" onClick={() => void load({ date, status })} disabled={busy}>
            Terapkan filter
          </button>
          <div className="report-actions">
            <a href={exportHref}>Unduh CSV</a>
            <button type="button" onClick={() => window.print()}>Cetak laporan</button>
          </div>
        </div>
        {error ? <p className="helper" style={{ color: '#a30f2d', marginTop: 8 }}>{error}</p> : null}
      </section>

      <div className="report-paper">
        <div className="report-head">
          <div>
            <div className="eyebrow">{companyName}</div>
            <h2>{title}</h2>
            <p className="helper" style={{ margin: '6px 0 0' }}>
              Periode {date ? formatDateDisplay(date) : 'semua tanggal'} • Status {status} • Total{' '}
              {formatNumber(data.total)} baris
            </p>
          </div>
          <div className="report-timestamp">
            <div className="helper">Dicetak</div>
            <b className="mono" style={{ fontSize: 12 }}>
              {printedAt || '—'}
            </b>
            <div className="helper" style={{ marginTop: 4 }}>
              Sumber sinkronisasi {summary.lastSyncAt.slice(11, 19)} UTC
            </div>
          </div>
        </div>

        <div className="kpis report-kpis">
          <div className="kpi kpi-highlight">
            <span className="label">Total Baris</span>
            <strong>{formatNumber(totals.total)}</strong>
            <p className="meta">Hasil filter aktif</p>
          </div>
          <div className="kpi">
            <span className="label">Direct OK</span>
            <strong style={{ color: '#0b6b46' }}>{formatNumber(totals.directOk)}</strong>
            <p className="meta">Lolos gerbang akhir</p>
          </div>
          <div className="kpi">
            <span className="label">After Rework</span>
            <strong style={{ color: '#a34f00' }}>{formatNumber(totals.afterRework)}</strong>
            <p className="meta">Lulus perbaikan</p>
          </div>
          <div className="kpi">
            <span className="label">NG</span>
            <strong style={{ color: '#a30f2d' }}>{formatNumber(totals.ng)}</strong>
            <p className="meta">Karantina / scrap</p>
          </div>
        </div>

        <h3 id="report-summary-heading">Rekap harian</h3>
        <div className="report-table-scroll" role="region" tabIndex={0} aria-labelledby="report-summary-heading">
        <table className="report-summary-table">
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
                <td>{formatDateDisplay(row.isoDate)}</td>
                <td>{row.dayName}</td>
                <td className="num">{formatNumber(row.total)}</td>
                <td className="num">{formatNumber(row.directOk)}</td>
                <td className="num">{formatNumber(row.afterRework)}</td>
                <td className="num">{formatNumber(row.ngCount)}</td>
                <td className="num">{formatNumber(row.innerBoxes)}</td>
                <td className="num">{formatNumber(row.outerBoxes)}</td>
                <td className="num">{formatPercent(row.directOk, row.total)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        </div>

        <h3 id="report-details-heading">Detail traceability</h3>
        {data.items.length === 0 ? (
          <EmptyState>Tidak ada baris untuk periode ini.</EmptyState>
        ) : (
          <div className="report-table-scroll" role="region" tabIndex={0} aria-labelledby="report-details-heading">
          <table className="report-detail-table">
            <thead>
              <tr>
                <th style={{ width: 36 }}>No</th>
                {activeColumns.map((column) => (
                  <th key={column.key}>{column.label}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.items.slice(0, PREVIEW_LIMIT).map((record, index) => (
                <tr key={record.id}>
                  <td className="num">{index + 1}</td>
                  {activeColumns.map((column) => (
                    <td key={column.key} className={column.key === 'no' ? 'num' : undefined}>
                      {column.key === 'isoDate' ? formatDateDisplay(record.isoDate) : cellText(record, column.key)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          </div>
        )}
        {data.items.length > PREVIEW_LIMIT ? (
          <p className="helper" style={{ marginTop: 10 }}>
            Pratinjau dibatasi {PREVIEW_LIMIT} baris. Unduh CSV untuk mendapatkan seluruh {formatNumber(data.total)} baris.
          </p>
        ) : null}
      </div>

      <Panel
        className="no-print"
        title="Pemilih kolom export"
        actions={
          <>
            <button type="button" onClick={selectAll}>
              Pilih semua
            </button>
            <button type="button" onClick={selectDefault}>
              Default
            </button>
          </>
        }
      >
        <div className="chip-row">
          {ALL_EXPORT_COLUMNS.map((column) => {
            const active = columns.includes(column.key);
            return (
              <button
                key={column.key}
                type="button"
                className={cn('chip', active ? 'ok' : '')}
                onClick={() => toggleColumn(column.key)}
                aria-pressed={active}
              >
                {active ? '✓ ' : ''}
                {column.label}
              </button>
            );
          })}
        </div>
        <p className="helper" style={{ marginTop: 12, marginBottom: 0 }}>
          Kolom terpilih ikut terkirim ke endpoint <code>/api/v1/traceability/export</code> dan ekspor memakai pemisah koma
          dengan BOM UTF-8 agar aman dibuka di Excel.
        </p>
      </Panel>

      {templates.length > 0 ? (
        <Panel className="no-print" title="Template laporan tersimpan">
          <div className="chip-row">
            {templates.map((template) => (
              <button
                key={template.name}
                type="button"
                className="chip"
                onClick={() => setColumns(template.columns.filter((key) => ALL_EXPORT_COLUMNS.some((column) => column.key === key)))}
              >
                {template.name}
                <b>{template.columns.length} kolom</b>
              </button>
            ))}
          </div>
        </Panel>
      ) : null}
    </>
  );
}
