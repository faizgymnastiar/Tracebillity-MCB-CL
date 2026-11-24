'use client';

import { scanInputProps } from '@/lib/scanInput';
import { ScanCodePreview } from './ScanCodePreview';
import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  BridgePayload,
  ScanMode,
  ScanResult,
  ScanSnapshot,
} from '@/types';
import { beep } from '@/lib/beep';
import { cn } from '@/lib/cn';
import { downloadTextFile } from '@/lib/download';
import { formatDateDisplay, formatNumber } from '@/lib/formatters';
import { Icon } from '@/components/ui/Icon';
import { MatrixCanvas } from '@/components/ui/MatrixCanvas';
import { EmptyState, KpiCard, PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';

interface ScanWorkspaceProps {
  initialSnapshot: ScanSnapshot;
  initialMode: ScanMode;
}

interface AlertState {
  tone: 'ok' | 'error' | 'rework';
  text: string;
  stale?: boolean;
}

const MODE_META: Record<ScanMode, { tab: string; tone: 'ok' | 'ng' | 'rw'; hint: string }> = {
  OK: { tab: 'Scan OK', tone: 'ok', hint: 'Produk lolos uji akhir masuk ke inner box aktif.' },
  NG: { tab: 'Scan NG', tone: 'ng', hint: 'Unit reject dikarantina dengan station & defect terlacak.' },
  REWORK: { tab: 'After Rework', tone: 'rw', hint: 'Validasi unit setelah perbaikan sebelum kembali ke lini.' },
};

const DISPOSITIONS = ['REWORK', 'SCRAP', 'HOLD QA'];

function labelTitleOf(snapshot: ScanSnapshot, mode: ScanMode): string {
  if (mode === 'NG') return snapshot.ui.scanNgTitle;
  if (mode === 'REWORK') return snapshot.ui.scanRwTitle;
  return snapshot.ui.scanOkTitle;
}

function bridgeMime(filename: string): string {
  if (filename.endsWith('.json')) return 'application/json;charset=utf-8';
  if (filename.endsWith('.csv')) return 'text/csv;charset=utf-8';
  return 'text/plain;charset=utf-8';
}

export function ScanWorkspace({ initialSnapshot, initialMode }: ScanWorkspaceProps) {
  const [snapshot, setSnapshot] = useState<ScanSnapshot>(initialSnapshot);
  const [mode, setMode] = useState<ScanMode>(initialMode);
  const [snInput, setSnInput] = useState('');
  const [stationId, setStationId] = useState(initialSnapshot.ngStations[0]?.id ?? '');
  const [defect, setDefect] = useState(initialSnapshot.ngStations[0]?.defects[0] ?? '');
  const [disposition, setDisposition] = useState(DISPOSITIONS[0]);
  const [reworkAction, setReworkAction] = useState(initialSnapshot.reworks[0] ?? '');
  const [verdict, setVerdict] = useState<'PASS' | 'FAIL'>('PASS');
  const [alert, setAlert] = useState<AlertState | null>(null);
  const [busy, setBusy] = useState(false);
  const scanInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!busy) scanInput.current?.focus();
  }, [busy, mode]);

  const production = snapshot.production ?? null;

  /** Hak operasi membatasi tab yang boleh dibuka (Fase 1: hak terpisah per mode). */
  const allowedModes = useMemo(() => {
    if (!snapshot.authEnforced || !snapshot.permissions || snapshot.permissions.length === 0) {
      return (Object.keys(MODE_META) as ScanMode[]);
    }
    return (Object.keys(MODE_META) as ScanMode[]).filter((item) => {
      const needed = item === 'OK' ? 'scan.unit' : 'ng.record';
      return snapshot.permissions!.includes(needed);
    });
  }, [snapshot.authEnforced, snapshot.permissions]);

  const station = useMemo(
    () => snapshot.ngStations.find((item) => item.id === stationId) ?? snapshot.ngStations[0],
    [snapshot.ngStations, stationId],
  );

  const innerRatio = snapshot.innerCap > 0 ? snapshot.activeInnerQty / snapshot.innerCap : 0;
  const outerRatio = snapshot.outerCap > 0 ? snapshot.unassignedInnerQty / snapshot.outerCap : 0;
  const totalToday = snapshot.kpis.ok + snapshot.kpis.ng + snapshot.kpis.rework;

  const applySnapshot = useCallback((next: ScanSnapshot) => setSnapshot(next), []);

  /** Versi konteks layar — backend menolak transaksi bila sudah usang. */
  const contextHeaders = useCallback((): Record<string, string> => {
    const version = snapshot.production?.contextVersion;
    return typeof version === 'number' ? { 'x-trace-context-version': String(version) } : {};
  }, [snapshot.production?.contextVersion]);

  const staleAlert = (payload: { error?: string; code?: string }): AlertState => ({
    tone: 'error',
    text:
      payload.code === 'CONTEXT_STALE'
        ? `${payload.error} Klik muat ulang untuk mengambil konteks terbaru.`
        : payload.error ?? 'Transaksi ditolak backend.',
    stale: payload.code === 'CONTEXT_STALE' || payload.code === 'NO_PRODUCTION_SESSION',
  });

  const runScan = useCallback(
    async (body: Record<string, string>): Promise<void> => {
      setBusy(true);
      try {
        const response = await fetch('/api/v1/traceability/scans', {
          method: 'POST',
          headers: { 'content-type': 'application/json', ...contextHeaders() },
          body: JSON.stringify(body),
        });
        const result = (await response.json()) as ScanResult | { error: string; code?: string };
        if ('error' in result) {
          setAlert(staleAlert(result));
          beep('error');
          return;
        }
        if (result.snapshot) applySnapshot(result.snapshot);
        setAlert({ tone: result.tone, text: result.message });
        beep(result.tone);
        if (result.autoBridge) {
          downloadTextFile(result.autoBridge.filename, result.autoBridge.content, bridgeMime(result.autoBridge.filename));
        }
      } catch {
        setAlert({ tone: 'error', text: 'Gagal menghubungi API scan. Periksa koneksi lalu ulangi.' });
        beep('error');
      } finally {
        setBusy(false);
      }
    },
    [applySnapshot, contextHeaders],
  );

  const submitOk = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!snInput.trim()) return;
    await runScan({ mode: 'OK', sn: snInput });
    setSnInput('');
  };

  const submitNg = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!snInput.trim()) return;
    await runScan({
      mode: 'NG',
      sn: snInput,
      stationId: station?.id ?? '',
      reason: defect,
      disposition,
    });
    setSnInput('');
  };

  const submitRework = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!snInput.trim()) return;
    await runScan({ mode: 'REWORK', sn: snInput, action: reworkAction, verdict });
    setSnInput('');
  };

  const postAction = async (body: Record<string, string>): Promise<void> => {
    setBusy(true);
    try {
      const response = await fetch('/api/v1/traceability/scan-state', {
        method: 'POST',
        headers: { 'content-type': 'application/json', ...contextHeaders() },
        body: JSON.stringify(body),
      });
      const next = (await response.json()) as ScanSnapshot | { error: string; code?: string };
      if ('error' in next) {
        setAlert(staleAlert(next));
        return;
      }
      applySnapshot(next);
    } catch {
      setAlert({ tone: 'error', text: 'Aksi gagal dijalankan. Coba lagi.' });
    } finally {
      setBusy(false);
    }
  };

  const handlePrint = async (): Promise<void> => {
    await postAction({ action: 'print' });
    window.print();
  };

  const handleBridge = async (): Promise<void> => {
    setBusy(true);
    try {
      const response = await fetch('/api/v1/traceability/bridge');
      const payload = (await response.json()) as BridgePayload | { error: string };
      if ('error' in payload) {
        setAlert({ tone: 'error', text: payload.error });
        return;
      }
      downloadTextFile(payload.filename, payload.content, bridgeMime(payload.filename));
      setAlert({ tone: 'ok', text: `Bridge ${payload.mode} siap: ${payload.filename}` });
    } catch {
      setAlert({ tone: 'error', text: 'Bridge printer tidak dapat diunduh.' });
    } finally {
      setBusy(false);
    }
  };

  const refreshState = async (): Promise<void> => {
    setBusy(true);
    try {
      const response = await fetch('/api/v1/traceability/scan-state', {
        cache: 'no-store',
        headers: contextHeaders(),
      });
      const next = (await response.json()) as ScanSnapshot | { error: string };
      if ('error' in next) {
        setAlert({ tone: 'error', text: next.error });
        return;
      }
      applySnapshot(next);
      setAlert((current) => (current?.stale ? null : current));
    } catch {
      setAlert({ tone: 'error', text: 'Snapshot scan gagal dimuat ulang.' });
    } finally {
      setBusy(false);
    }
  };

  /** Ganti WO aktif dari layar scan; konteks sesi berubah dan versi naik. */
  const switchWorkOrder = async (number: string): Promise<void> => {
    setBusy(true);
    try {
      const response = await fetch('/api/v1/production/active-work-order', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ number }),
      });
      if (!response.ok) {
        const payload = (await response.json().catch(() => ({}))) as { error?: string };
        setAlert({ tone: 'error', text: payload.error ?? 'Ganti WO gagal.' });
        return;
      }
      await refreshState();
      setAlert({ tone: 'ok', text: `WO aktif berpindah ke ${number}. Muat ulang layar lain yang masih memakai konteks lama.` });
    } catch {
      setAlert({ tone: 'error', text: 'Ganti WO gagal terhubung ke server.' });
    } finally {
      setBusy(false);
    }
  };

  const handleStationChange = (nextId: string): void => {
    setStationId(nextId);
    const nextStation = snapshot.ngStations.find((item) => item.id === nextId);
    setDefect(nextStation?.defects[0] ?? '');
    setMode('NG');
  };

  const selectMode = (next: ScanMode): void => {
    setMode(next);
    setAlert(null);
  };

  const labels = [snapshot.labels.inner, snapshot.labels.outer];

  return (
    <>
      <PageHeader
        eyebrow="TRACEABILITY • END-OF-LINE GATE"
        title={labelTitleOf(snapshot, mode)}
        description={`${snapshot.activeDayName}, ${formatDateDisplay(snapshot.activeDate)} — ${formatNumber(totalToday)} unit tercatat hari ini.`}
        actions={
          <div className="panel-actions">
            <StatusBadge tone="ok">{snapshot.ui.scanStatusBadge}</StatusBadge>
            <StatusBadge tone="neutral">{snapshot.bridgeMode}</StatusBadge>
            <button type="button" className="topbar-icon" style={{ background: '#eef2f7', color: '#161b41', borderColor: '#cbd5e1' }}
              onClick={refreshState} disabled={busy} aria-label="Muat ulang snapshot scan">
              <Icon name="refresh" size={14} />
            </button>
          </div>
        }
      />

      <section className="context-strip">
        <div className="context-item">
          <small>Work Order{production ? ` — aktif (v${production.contextVersion})` : ''}</small>
          <b className="mono">{snapshot.context.workOrder || '—'}</b>
        </div>
        {production && production.workOrders.length > 1 ? (
          <div className="context-item">
            <small>Ganti WO</small>
            <select
              value={production.activeWorkOrderNumber ?? ''}
              onChange={(event) => void switchWorkOrder(event.target.value)}
              disabled={busy}
              aria-label="Ganti WO aktif"
            >
              {production.workOrders.map((wo) => (
                <option key={wo.id} value={wo.number}>{wo.number} — {wo.client}</option>
              ))}
            </select>
          </div>
        ) : null}
        <div className="context-item">
          <small>Klien / Tujuan</small>
          <b>{production?.workOrderClient || snapshot.context.destination || '—'}</b>
        </div>
        {production?.targetQty ? (
          <div className="context-item">
            <small>Target WO</small>
            <b className="mono">{formatNumber(production.targetQty)} pcs</b>
          </div>
        ) : null}
        <div className="context-item">
          <small>Material Lot</small>
          <b className="mono">{snapshot.context.materialLot || '—'}</b>
        </div>
        <div className="context-item">
          <small>Line / Mesin</small>
          <b>{snapshot.context.line || '—'}</b>
        </div>
        <div className="context-item">
          <small>Shift</small>
          <b>{snapshot.context.shift || '—'}</b>
        </div>
        <div className="context-item">
          <small>Model / SKU</small>
          <b>{snapshot.context.sku || '—'}</b>
        </div>
        <div className="context-item">
          <small>Operator</small>
          <b>{snapshot.context.operator || '—'}</b>
        </div>
      </section>

      {snapshot.authEnforced && !production ? (
        <div className="scan-alert error" role="status">
          Belum ada sesi produksi aktif.{' '}
          <Link href="/" className="link">Mulai produksi di beranda</Link>{' '}
          sebelum menyimpan transaksi scan.
        </div>
      ) : null}

      <div className="seg-tabs" role="tablist" aria-label="Mode scan">
        {(Object.keys(MODE_META) as ScanMode[])
          .filter((item) => allowedModes.includes(item))
          .map((item) => {
          const meta = MODE_META[item];
          const count = item === 'OK' ? snapshot.kpis.ok : item === 'NG' ? snapshot.kpis.ng : snapshot.kpis.rework;
          return (
            <button
              key={item}
              type="button"
              role="tab"
              aria-selected={mode === item}
              className={cn('seg-tab', mode === item && 'active', mode === item && `tone-${meta.tone}`)}
              onClick={() => selectMode(item)}
            >
              {meta.tab}
              <span className="badge neutral">{formatNumber(count)}</span>
            </button>
          );
        })}
      </div>

      {alert ? (
        <div className={cn('scan-alert', alert.tone)} role="status">
          {alert.text}
        </div>
      ) : null}

      <section className="split-grid">
        <div className="stack">
          <Panel
            title={MODE_META[mode].tab}
            actions={<span className="helper">{MODE_META[mode].hint}</span>}
          >
            {mode === 'OK' ? (
              <form className="scan-form" onSubmit={submitOk}>
                <ScanCodePreview value={snInput} />
                <label className="grow">
                  Serial Number
                  <input {...scanInputProps}
                    className="scan-input mono"
                    value={snInput}
                    onChange={(event) => setSnInput(event.target.value.toUpperCase())}
                    placeholder="SCAN SERIAL NUMBER DI SINI"
                    autoComplete="off"
                    ref={scanInput}
                  />
                </label>
                <button type="submit" className="btn-primary" disabled={busy || !snInput.trim()}>
                  <Icon name="check" size={14} /> Simpan OK
                </button>
              </form>
            ) : null}

            {mode === 'NG' ? (
              <form className="scan-form" onSubmit={submitNg}>
                <ScanCodePreview value={snInput} />
                <label className="grow">
                  Serial Number
                  <input {...scanInputProps}
                    className="scan-input mono"
                    value={snInput}
                    onChange={(event) => setSnInput(event.target.value.toUpperCase())}
                    placeholder="SCAN SERIAL NUMBER REJECT"
                    autoComplete="off"
                    ref={scanInput}
                  />
                </label>
                <label style={{ flex: '1 1 190px' }}>
                  Station
                  <select value={station?.id ?? ''} onChange={(event) => handleStationChange(event.target.value)}>
                    {snapshot.ngStations.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.label}
                      </option>
                    ))}
                  </select>
                </label>
                <label style={{ flex: '1 1 210px' }}>
                  Defect
                  <select value={defect} onChange={(event) => setDefect(event.target.value)}>
                    {(station?.defects ?? []).map((item) => (
                      <option key={item} value={item}>
                        {item}
                      </option>
                    ))}
                  </select>
                </label>
                <label style={{ flex: '1 1 150px' }}>
                  Disposition
                  <select value={disposition} onChange={(event) => setDisposition(event.target.value)}>
                    {DISPOSITIONS.map((item) => (
                      <option key={item} value={item}>
                        {item}
                      </option>
                    ))}
                  </select>
                </label>
                <button type="submit" className="btn-primary" disabled={busy || !snInput.trim()}>
                  <Icon name="warning" size={14} /> Karantina NG
                </button>
              </form>
            ) : null}

            {mode === 'REWORK' ? (
              <form className="scan-form" onSubmit={submitRework}>
                <ScanCodePreview value={snInput} />
                <label className="grow">
                  Serial Number
                  <input {...scanInputProps}
                    className="scan-input mono"
                    value={snInput}
                    onChange={(event) => setSnInput(event.target.value.toUpperCase())}
                    placeholder="SCAN SN SETELAH REWORK"
                    autoComplete="off"
                    ref={scanInput}
                  />
                </label>
                <label style={{ flex: '1 1 260px' }}>
                  Tindakan Rework
                  <select value={reworkAction} onChange={(event) => setReworkAction(event.target.value)}>
                    {snapshot.reworks.map((item) => (
                      <option key={item} value={item}>
                        {item}
                      </option>
                    ))}
                  </select>
                </label>
                <label style={{ flex: '0 1 130px' }}>
                  Hasil Uji
                  <select value={verdict} onChange={(event) => setVerdict(event.target.value === 'FAIL' ? 'FAIL' : 'PASS')}>
                    <option value="PASS">PASS</option>
                    <option value="FAIL">FAIL</option>
                  </select>
                </label>
                <button type="submit" className="btn-primary" disabled={busy || !snInput.trim()}>
                  <Icon name="refresh" size={14} /> Verifikasi
                </button>
              </form>
            ) : null}

            <div className="stat-inline" style={{ marginTop: 14 }}>
              <span>
                Ticket rework berikutnya <b className="mono">{snapshot.nextReworkTicket}</b>
              </span>
              <span>
                Batas siklus <b>{snapshot.maxReworkCycles}x</b>
              </span>
              <span>
                Inner aktif <b className="mono">{snapshot.activeInnerId}</b>
              </span>
            </div>
          </Panel>

          <Panel
            title={`Batch aktif — ${snapshot.activeInnerId}`}
            actions={
              <>
                <StatusBadge tone="neutral">
                  {snapshot.activeInnerQty} / {snapshot.innerCap} Pcs
                </StatusBadge>
                <button
                  type="button"
                  onClick={() => void postAction({ action: 'clear' })}
                  disabled={busy || snapshot.currentBatch.length === 0}
                >
                  <Icon name="trash" size={14} /> Kosongkan
                </button>
              </>
            }
          >
            {snapshot.currentBatch.length === 0 ? (
              <EmptyState>Belum ada unit di batch aktif. Scan produk OK untuk mulai mengisi inner box.</EmptyState>
            ) : (
              <div className="list">
                {snapshot.currentBatch.map((item) => (
                  <div key={item.sn} className="list-row is-active">
                    <div>
                      <b className="mono">{item.sn}</b>
                      <p>
                        {item.sku} • {item.operator} • {item.time}
                      </p>
                    </div>
                    <div className="list-tail">
                      {item.reworked ? <span className="badge rw">RWK #{item.reworkCycle}</span> : <span className="badge ok">OK</span>}
                      <div style={{ marginTop: 6 }}>
                        <button
                          type="button"
                          className="link"
                          onClick={() => void postAction({ action: 'removeItem', sn: item.sn })}
                          disabled={busy}
                        >
                          Hapus
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Panel>
        </div>

        <div className="stack">
          <div className="kpis">
            <KpiCard label="Output OK" value={formatNumber(snapshot.kpis.ok)} meta="Lulus gerbang akhir" tone="ok" highlight />
            <KpiCard label="Karantina NG" value={formatNumber(snapshot.kpis.ng)} meta="Menunggu disposisi" tone="ng" />
            <KpiCard label="After Rework" value={formatNumber(snapshot.kpis.rework)} meta="Tervalidasi ulang" tone="rework" />
          </div>

          <Panel title="Kapasitas kemasan">
            <div className="meter-label">
              <span>Inner box aktif</span>
              <b className="mono">
                {snapshot.activeInnerQty} / {snapshot.innerCap}
              </b>
            </div>
            <div className="meter" role="progressbar" aria-valuenow={snapshot.activeInnerQty} aria-valuemin={0}
              aria-valuemax={snapshot.innerCap}>
              <span style={{ width: `${Math.min(100, Math.round(innerRatio * 100))}%` }} />
            </div>
            <div className="meter-label">
              <span>Inner siap outer</span>
              <b className="mono">
                {snapshot.unassignedInnerQty} / {snapshot.outerCap}
              </b>
            </div>
            <div className="meter tone-rw" role="progressbar" aria-valuenow={snapshot.unassignedInnerQty}
              aria-valuemin={0} aria-valuemax={snapshot.outerCap}>
              <span style={{ width: `${Math.min(100, Math.round(outerRatio * 100))}%` }} />
            </div>
            <p className="helper" style={{ marginTop: 12, marginBottom: 0 }}>
              Outer box berikutnya <b className="mono">{snapshot.activeOuterId}</b> • {snapshot.outerCap * snapshot.innerCap} Pcs
              per outer.
            </p>
          </Panel>

          <Panel
            title="Riwayat barcode hari ini"
            actions={<span className="helper">{snapshot.history.length} entri</span>}
          >
            {snapshot.history.length === 0 ? (
              <EmptyState>Belum ada inner/outer box yang disegel hari ini.</EmptyState>
            ) : (
              <div className="list">
                {snapshot.history.map((entry) => (
                  <div key={`${entry.type}-${entry.id}-${entry.event}-${entry.time}`} className="list-row">
                    <div>
                      <b className="mono">{entry.id}</b>
                      <p>
                        {entry.type} • {entry.sku} • {entry.qty} Pcs
                      </p>
                    </div>
                    <div className="list-tail">
                      <span className={cn('badge', entry.event === 'PRINT' ? 'neutral' : 'ok')}>{entry.event}</span>
                      <p className="mono">{entry.time.replace(/^.*?(\d{2}\/\d{2}\/\d{4})/, '$1')}</p>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Panel>
        </div>
      </section>

      <Panel
        className="no-print"
        title="Label kemasan & printer bridge"
        actions={
          <>
            <button type="button" onClick={() => void handlePrint()} disabled={busy}>
              <Icon name="print" size={14} /> Cetak label
            </button>
            <button type="button" onClick={() => void handleBridge()} disabled={busy}>
              <Icon name="download" size={14} /> Unduh bridge
            </button>
          </>
        }
      >
        <div className="split-grid">
          {labels.map((label, index) => (
            <div className="label-sheet" key={label.code}>
              <div className="label-sheet-head">
                <b>{index === 0 ? snapshot.brand.labelInnerTitle : snapshot.brand.labelOuterTitle}</b>
                <span className="helper mono">{label.printedAt}</span>
              </div>
              <div className="label-body">
                <MatrixCanvas payload={label.payload} mode={snapshot.barcode.type} size={snapshot.barcode.size} />
                <dl className="label-meta">
                  <dt>Kode</dt>
                  <dd className="label-code">{label.code}</dd>
                  <dt>SKU</dt>
                  <dd>{label.sku}</dd>
                  <dt>WO</dt>
                  <dd className="mono">{label.workOrder}</dd>
                  <dt>Lot</dt>
                  <dd className="mono">{label.materialLot}</dd>
                  <dt>Qty</dt>
                  <dd>{label.qtyText}</dd>
                  <dt>Tujuan</dt>
                  <dd>{label.destination}</dd>
                  <dt>Line</dt>
                  <dd>{snapshot.context.line}</dd>
                  <dt>Operator</dt>
                  <dd>{snapshot.context.operator}</dd>
                </dl>
              </div>
            </div>
          ))}
        </div>
        <p className="helper" style={{ marginBottom: 0 }}>
          Ukuran label {snapshot.barcode.labelWidthMm} x {snapshot.barcode.labelHeightMm} mm — format bridge{' '}
          {snapshot.bridgeMode}. Payload 2D mengikuti template delimiter {snapshot.context.materialLot ? 'aktif' : 'default'}.
        </p>
      </Panel>

      <div className="printable-labels">
        <div className="split-grid">
          {labels.map((label, index) => (
            <div className="label-sheet" key={`print-${label.code}`}>
              <div className="label-sheet-head">
                <b>{index === 0 ? snapshot.brand.labelInnerTitle : snapshot.brand.labelOuterTitle}</b>
                <span className="helper mono">{label.printedAt}</span>
              </div>
              <div className="label-body">
                <MatrixCanvas payload={label.payload} mode={snapshot.barcode.type} size={snapshot.barcode.size} />
                <dl className="label-meta">
                  <dt>Kode</dt>
                  <dd className="label-code">{label.code}</dd>
                  <dt>SKU</dt>
                  <dd>{label.sku}</dd>
                  <dt>WO</dt>
                  <dd className="mono">{label.workOrder}</dd>
                  <dt>Lot</dt>
                  <dd className="mono">{label.materialLot}</dd>
                  <dt>Qty</dt>
                  <dd>{label.qtyText}</dd>
                  <dt>Tujuan</dt>
                  <dd>{label.destination}</dd>
                </dl>
              </div>
            </div>
          ))}
        </div>
      </div>

      <section className="split-grid">
        <Panel
          title="Inner box selesai"
          actions={
            <Link href="/modules/traceability/database" className="helper">
              Buka database
            </Link>
          }
        >
          {snapshot.completedInners.length === 0 ? (
            <EmptyState>Belum ada inner box tersegel hari ini.</EmptyState>
          ) : (
            <div className="list">
              {snapshot.completedInners
                .slice()
                .reverse()
                .map((box) => (
                  <div className="list-row" key={box.id}>
                    <div>
                      <b className="mono">{box.id}</b>
                      <p>
                        {box.sku} • {box.items.length} Pcs • {box.time}
                      </p>
                    </div>
                    <div className="list-tail">
                      <Link href={`/modules/traceability/database/${encodeURIComponent(box.id)}`}>Cek Isi</Link>
                      {box.partial ? <div className="badge rw">PARTIAL</div> : null}
                    </div>
                  </div>
                ))}
            </div>
          )}
        </Panel>

        <Panel title="Outer box selesai">
          {snapshot.completedOuters.length === 0 ? (
            <EmptyState>Outer box terbentuk otomatis setelah {snapshot.outerCap} inner box selesai.</EmptyState>
          ) : (
            <div className="list">
              {snapshot.completedOuters
                .slice()
                .reverse()
                .map((box) => (
                  <div className="list-row" key={box.id}>
                    <div>
                      <b className="mono">{box.id}</b>
                      <p>
                        {box.sku} • {box.inners.length} Inner • {box.totalPcs} Pcs
                      </p>
                    </div>
                    <div className="list-tail">
                      <Link href={`/modules/traceability/database/${encodeURIComponent(box.id)}`}>Cek Isi</Link>
                      {box.partial ? <div className="badge rw">PARTIAL</div> : null}
                    </div>
                  </div>
                ))}
            </div>
          )}
        </Panel>
      </section>

      <section className="split-grid">
        <Panel title="Karantina NG hari ini" actions={<StatusBadge tone="ng">{snapshot.ngToday.length} unit</StatusBadge>}>
          {snapshot.ngToday.length === 0 ? (
            <EmptyState>Tidak ada unit NG hari ini.</EmptyState>
          ) : (
            <div className="list">
              {snapshot.ngToday
                .slice()
                .reverse()
                .map((item) => (
                  <div className="list-row" key={`${item.sn}-${item.time}`}>
                    <div>
                      <b className="mono">{item.sn}</b>
                      <p>
                        {item.stationLabel} — {item.reason}
                      </p>
                    </div>
                    <div className="list-tail">
                      <span className="badge ng">{item.disposition}</span>
                      <p className="mono">{item.time}</p>
                    </div>
                  </div>
                ))}
            </div>
          )}
        </Panel>

        <Panel title="Rework tervalidasi" actions={<StatusBadge tone="rework">{snapshot.reworkToday.length} unit</StatusBadge>}>
          {snapshot.reworkToday.length === 0 ? (
            <EmptyState>Belum ada unit yang lulus rework hari ini.</EmptyState>
          ) : (
            <div className="list">
              {snapshot.reworkToday
                .slice()
                .reverse()
                .map((item) => (
                  <div className="list-row" key={item.ticketId}>
                    <div>
                      <b className="mono">{item.sn}</b>
                      <p>
                        {item.prevDefect} → {item.action}
                      </p>
                    </div>
                    <div className="list-tail">
                      <span className="badge rw">
                        {item.ticketId} • #{item.cycle}
                      </span>
                      <p className="mono">{item.time}</p>
                    </div>
                  </div>
                ))}
            </div>
          )}
        </Panel>
      </section>
    </>
  );
}
