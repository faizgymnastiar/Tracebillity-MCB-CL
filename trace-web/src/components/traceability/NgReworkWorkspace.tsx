'use client';

import { scanInputProps } from '@/lib/scanInput';
import { ScanCodePreview } from './ScanCodePreview';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  DataTableColumn,
  NgStationInfo,
  NgUnitDetail,
  NgUnitInfo,
  NgUnitStatus,
  NgWorkspaceSummary,
  OkScanResult,
  PagedResponse,
  ProductionContext,
  RecordNgResult,
  RecordReworkResult,
} from '@/types';
import { beep } from '@/lib/beep';
import { cn } from '@/lib/cn';
import { formatWibDateTime } from '@/lib/datetime';
import { formatNumber } from '@/lib/formatters';
import { DataTable } from '@/components/ui/DataTable';
import { Icon } from '@/components/ui/Icon';
import { EmptyState, KpiCard, Notice, PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';
import type { Tone } from '@/components/ui/SharedUi';

type TabKey = 'ng' | 'rework' | 'ok' | 'history';
type OkMode = 'SERIAL' | 'REWORK';

interface NgReworkWorkspaceProps {
  initialSummary: NgWorkspaceSummary;
  initialStations: NgStationInfo[];
  /** Versi konteks sesi produksi; backend menolak transaksi bila sudah basi. */
  contextVersion: number | null;
  production: ProductionContext | null;
  /** Daftar tindakan rework dari konfigurasi (mis. "RW-01: Kalibrasi Ulang"). */
  reworkActions: string[];
}

interface AlertState {
  tone: 'ok' | 'error' | 'rework';
  text: string;
  /** Konteks produksi basi: tampilkan tombol muat ulang. */
  stale?: boolean;
  /** Label internal yang harus ditempel/dilepas — hanya setelah transaksi dinyatakan sah. */
  chip?: string;
  removeLabel?: boolean;
}

interface PairDraft {
  label: string;
  serial: string;
}

interface QueueProps {
  title: string;
  units: NgUnitInfo[];
  tone: Tone;
  empty: string;
  actionLabel?: string;
  onAction?: (unit: NgUnitInfo) => void;
}

interface TraceStep {
  at: string;
  label: string;
  detail: string;
  tone: 'ok' | 'ng' | 'rework' | 'neutral';
}

const TAB_META: Record<TabKey, { label: string; hint: string; tone: string }> = {
  ng: { label: 'Scan NG', hint: 'QR internal NG wajib; satu pos + satu defect per kejadian.', tone: 'ng' },
  rework: { label: 'Rework selesai', hint: 'Setiap rework menambah satu percobaan.', tone: 'rw' },
  ok: { label: 'OK akhir', hint: 'Mode serial saja atau pasangan label + serial.', tone: 'ok' },
  history: { label: 'Riwayat unit', hint: 'Jejak NG, rework, dan OK per serial.', tone: 'neutral' },
};

const STATUS_TONE: Record<NgUnitStatus, Tone> = {
  NG_QUARANTINE: 'ng',
  REWORK_RETEST: 'rework',
  QC_HOLD: 'ng',
  OK_FINAL: 'ok',
};

const STATUS_LABEL: Record<NgUnitStatus, string> = {
  NG_QUARANTINE: 'Karantina NG',
  REWORK_RETEST: 'Uji ulang',
  QC_HOLD: 'Tahan QC',
  OK_FINAL: 'OK akhir',
};

const STATUS_OPTIONS: Array<{ value: string; label: string }> = [
  { value: '', label: 'Semua status' },
  { value: 'NG_QUARANTINE', label: 'Karantina NG' },
  { value: 'REWORK_RETEST', label: 'Uji ulang' },
  { value: 'QC_HOLD', label: 'Tahan QC' },
  { value: 'OK_FINAL', label: 'OK akhir' },
];

const HISTORY_PAGE_SIZE = 25;

type PostOutcome<T> = { ok: true; data: T } | { ok: false; error: string; code?: string };

function newKey(prefix: string): string {
  const random =
    globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  return `${prefix}-${random}`.toUpperCase().slice(0, 120);
}

/**
 * Satu percobaan simpan = satu kunci. Retry dengan isi yang sama memakai kunci lama
 * (backend mengembalikan hasil lama), sedangkan scan berikutnya mendapat kunci baru.
 */
function useIdempotencyKey(prefix: string): { take: (signature: string) => string; reset: () => void } {
  const ref = useRef<{ signature: string; key: string }>({ signature: '', key: newKey(prefix) });
  const take = (signature: string): string => {
    if (ref.current.signature !== signature) ref.current = { signature, key: newKey(prefix) };
    return ref.current.key;
  };
  const reset = (): void => {
    ref.current = { signature: '', key: newKey(prefix) };
  };
  return { take, reset };
}

async function postJson<T>(
  path: string,
  body: Record<string, string>,
  contextVersion: number | null,
): Promise<PostOutcome<T>> {
  const headers: Record<string, string> = { 'content-type': 'application/json' };
  if (typeof contextVersion === 'number') headers['x-trace-context-version'] = String(contextVersion);

  let response: Response;
  try {
    response = await fetch(path, { method: 'POST', headers, body: JSON.stringify(body) });
  } catch {
    return {
      ok: false,
      error: 'Tidak dapat menghubungi server. Periksa koneksi lalu ulangi dengan data yang sama.',
    };
  }

  if (response.ok) {
    try {
      return { ok: true, data: (await response.json()) as T };
    } catch {
      return { ok: false, error: 'Respons server tidak dapat dibaca; periksa riwayat unit sebelum mengulang.' };
    }
  }

  try {
    const payload = (await response.json()) as { error?: string; code?: string };
    return { ok: false, error: payload.error ?? 'Transaksi ditolak backend.', code: payload.code };
  } catch {
    return { ok: false, error: `Transaksi ditolak backend (HTTP ${response.status}).` };
  }
}

function staleAlertOf(error: string, code?: string): AlertState {
  const stale = code === 'CONTEXT_STALE' || code === 'NO_PRODUCTION_SESSION';
  return {
    tone: 'error',
    text: stale ? `${error} Konteks produksi di layar sudah usang.` : error,
    stale,
  };
}

function statusLabelOf(unit: NgUnitInfo): string {
  return unit.statusLabel || STATUS_LABEL[unit.status] || unit.status;
}

function unitRefOf(unit: NgUnitInfo): string {
  return unit.labelPayload ?? unit.serial ?? unit.id;
}

function detailSteps(detail: NgUnitDetail): TraceStep[] {
  const steps: TraceStep[] = [];
  for (const event of detail.ngEvents) {
    steps.push({
      at: event.occurredAtUtc,
      label: `NG #${event.seq} — ${event.stageCode} ${event.stageName}`,
      detail:
        `${event.defectName} (${event.defectGroup}) • percobaan ${event.attemptNo} • ` +
        `${event.disposition === 'QC_HOLD' ? 'Tahan QC' : 'Rework'} • ${event.actorName} • ` +
        `${event.shiftCode}/${event.lineCode}${event.note ? ` • ${event.note}` : ''}`,
      tone: 'ng',
    });
  }
  for (const event of detail.reworkEvents) {
    steps.push({
      at: event.performedAtUtc,
      label: `Rework percobaan ${event.attemptNo}`,
      detail: `${event.action} • ${event.actorName}${event.note ? ` • ${event.note}` : ''}`,
      tone: 'rework',
    });
  }
  if (detail.okScan) {
    steps.push({
      at: detail.okScan.occurredAtUtc,
      label: `OK akhir — ${detail.okScan.mode === 'REWORK_PAIR' ? 'pasangan label' : 'serial saja'}`,
      detail:
        `${detail.okScan.serial} • WO ${detail.okScan.workOrderTargetNumber ?? '—'} • ` +
        `${detail.okScan.actorName}${detail.okScan.client ? ` • ${detail.okScan.client}` : ''}`,
      tone: 'ok',
    });
  }
  return steps.sort((left, right) => (left.at < right.at ? -1 : left.at > right.at ? 1 : 0));
}

function UnitQueue({ title, units, tone, empty, actionLabel, onAction }: QueueProps) {
  return (
    <Panel title={title} actions={<StatusBadge tone={tone}>{formatNumber(units.length)}</StatusBadge>}>
      {units.length === 0 ? (
        <EmptyState>{empty}</EmptyState>
      ) : (
        <div className="list">
          {units.map((unit) => (
            <div key={unit.id} className="list-row">
              <div>
                <b className="mono">{unitRefOf(unit)}</b>
                <p>
                  {unit.stageCode} • {formatWibDateTime(unit.lastEventAtUtc)}
                </p>
              </div>
              <div className="list-tail">
                <span className={cn('badge', tone === 'ok' ? 'ok' : tone === 'ng' ? 'ng' : 'rw')}>
                  {unit.attempts}/{unit.maxAttempts}
                </span>
                {actionLabel && onAction ? (
                  <div style={{ marginTop: 6 }}>
                    <button type="button" className="link" onClick={() => onAction(unit)}>
                      {actionLabel}
                    </button>
                  </div>
                ) : null}
              </div>
            </div>
          ))}
        </div>
      )}
    </Panel>
  );
}

/**
 * Layar "Karantina & Rework" (Fase 3). Layar hanya memanggil /api/v1/ng/**;
 * klasifikasi QR, aturan percobaan, dan transisi status tetap diputuskan backend.
 */
export function NgReworkWorkspace({
  initialSummary,
  initialStations,
  contextVersion,
  production,
  reworkActions,
  initialTab = 'ng',
}: NgReworkWorkspaceProps & { initialTab?: 'ng' | 'ok' }) {
  const [summary, setSummary] = useState<NgWorkspaceSummary>(initialSummary);
  const [tab, setTab] = useState<TabKey>(initialTab);
  const [alert, setAlert] = useState<AlertState | null>(null);
  const [busy, setBusy] = useState(false);

  const stations = initialStations;
  const [stationCode, setStationCode] = useState(stations[0]?.code ?? '');
  const [defectId, setDefectId] = useState(stations[0]?.defects[0]?.id ?? '');
  const [ngLabel, setNgLabel] = useState('');
  const [ngNote, setNgNote] = useState('');

  const [reworkRef, setReworkRef] = useState('');
  const [reworkAction, setReworkAction] = useState(reworkActions[0] ?? 'Perbaikan selesai');
  const [reworkNote, setReworkNote] = useState('');

  const [okMode, setOkMode] = useState<OkMode>('SERIAL');
  const [okSerial, setOkSerial] = useState('');
  const [okLabel, setOkLabel] = useState('');
  const [pair, setPair] = useState<PairDraft | null>(null);
  const [pairPreview, setPairPreview] = useState('');

  const [historySearch, setHistorySearch] = useState('');
  const [historyQuery, setHistoryQuery] = useState('');
  const [historyStatus, setHistoryStatus] = useState('');
  const [historyPage, setHistoryPage] = useState(1);
  const [historyTick, setHistoryTick] = useState(0);
  const [historyData, setHistoryData] = useState<PagedResponse<NgUnitInfo> | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [detail, setDetail] = useState<NgUnitDetail | null>(null);
  const [detailBusy, setDetailBusy] = useState(false);

  const ngInputRef = useRef<HTMLInputElement | null>(null);
  const reworkInputRef = useRef<HTMLInputElement | null>(null);
  const okSerialRef = useRef<HTMLInputElement | null>(null);
  const okLabelRef = useRef<HTMLInputElement | null>(null);
  const historyInputRef = useRef<HTMLInputElement | null>(null);

  const ngKey = useIdempotencyKey('NG');
  const reworkKey = useIdempotencyKey('RWK');
  const okKey = useIdempotencyKey('OK');

  const permissions = summary.permissions ?? [];
  const canRecordNg = permissions.includes('ng.record');
  const canScanUnit = permissions.includes('scan.unit');
  const canHistory = permissions.includes('history.read');

  const allowedTabs = useMemo(
    () =>
      (Object.keys(TAB_META) as TabKey[]).filter((item) => {
        if (item === 'ok') return canScanUnit;
        if (item === 'history') return canHistory;
        return canRecordNg;
      }),
    [canRecordNg, canScanUnit, canHistory],
  );
  const activeTab = allowedTabs.includes(tab) ? tab : allowedTabs[0];

  useEffect(() => {
    if (activeTab === 'ng') ngInputRef.current?.focus();
    if (activeTab === 'rework') reworkInputRef.current?.focus();
    if (activeTab === 'ok') (okMode === 'REWORK' ? okLabelRef : okSerialRef).current?.focus();
    if (activeTab === 'history') historyInputRef.current?.focus();
  }, [activeTab, okMode]);

  const station = stations.find((item) => item.code === stationCode) ?? stations[0];
  const stationDefects = station?.defects ?? [];
  const defect = stationDefects.find((item) => item.id === defectId) ?? stationDefects[0];

  const refreshSummary = useCallback(async (): Promise<void> => {
    try {
      const response = await fetch('/api/v1/ng/summary', { cache: 'no-store' });
      if (response.ok) setSummary((await response.json()) as NgWorkspaceSummary);
    } catch {
      // Server sedang mati: ringkasan terakhir tetap ditampilkan.
    }
  }, []);

  useEffect(() => {
    if (activeTab !== 'history') return;
    let cancelled = false;

    const query = new URLSearchParams({
      search: historyQuery,
      status: historyStatus,
      page: String(historyPage),
      pageSize: String(HISTORY_PAGE_SIZE),
    });

    setHistoryBusy(true);
    fetch(`/api/v1/ng/units?${query.toString()}`, { cache: 'no-store' })
      .then(async (response) => {
        if (cancelled) return;
        if (!response.ok) {
          setAlert(staleAlertOf('Daftar unit gagal dimuat.'));
          return;
        }
        setHistoryData((await response.json()) as PagedResponse<NgUnitInfo>);
      })
      .catch(() => {
        if (!cancelled) setAlert({ tone: 'error', text: 'Tidak dapat menghubungi server riwayat unit.' });
      })
      .finally(() => {
        if (!cancelled) setHistoryBusy(false);
      });

    return () => {
      cancelled = true;
    };
  }, [activeTab, historyQuery, historyStatus, historyPage, historyTick]);

  const fail = useCallback((error: string, code?: string): void => {
    setAlert(staleAlertOf(error, code));
    beep('error');
  }, []);

  const selectTab = (next: TabKey): void => {
    setTab(next);
    setAlert(null);
    setPair(null);
  };

  const submitNg = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    const label = ngLabel.trim();
    if (!label || !station || !defect) return;

    const key = ngKey.take([label, station.code, defect.id, ngNote.trim()].join('|'));
    setBusy(true);
    const outcome = await postJson<RecordNgResult>(
      '/api/v1/ng/records/ng',
      {
        label,
        stationCode: station.code,
        defectId: defect.id,
        note: ngNote.trim(),
        idempotencyKey: key,
      },
      contextVersion,
    );
    setBusy(false);

    if (!outcome.ok) {
      fail(outcome.error, outcome.code);
      return;
    }

    ngKey.reset();
    setNgLabel('');
    setNgNote('');
    const hold = outcome.data.unit.status === 'QC_HOLD';
    setAlert({
      tone: hold ? 'rework' : 'ok',
      text: outcome.data.message,
      chip: outcome.data.unit.labelPayload ?? outcome.data.labelPayload,
    });
    beep(hold ? 'rework' : 'ok');
    await refreshSummary();
    ngInputRef.current?.focus();
  };

  const submitRework = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    const unitRef = reworkRef.trim();
    if (!unitRef) return;
    const action = reworkAction.trim().slice(0, 60) || 'REWORK';

    const key = reworkKey.take([unitRef, action, reworkNote.trim()].join('|'));
    setBusy(true);
    const outcome = await postJson<RecordReworkResult>(
      '/api/v1/ng/records/rework',
      { unitRef, action, note: reworkNote.trim(), idempotencyKey: key },
      contextVersion,
    );
    setBusy(false);

    if (!outcome.ok) {
      fail(outcome.error, outcome.code);
      return;
    }

    reworkKey.reset();
    setReworkRef('');
    setReworkNote('');
    setAlert({ tone: 'ok', text: outcome.data.message });
    beep('ok');
    await refreshSummary();
    reworkInputRef.current?.focus();
  };

  const submitOkSerial = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    const serial = okSerial.trim();
    if (!serial) return;

    const key = okKey.take(['SERIAL', serial].join('|'));
    setBusy(true);
    const outcome = await postJson<OkScanResult>(
      '/api/v1/ng/records/ok',
      { mode: 'SERIAL', serial, label: '', note: '', idempotencyKey: key },
      contextVersion,
    );
    setBusy(false);

    if (!outcome.ok) {
      fail(outcome.error, outcome.code);
      return;
    }

    okKey.reset();
    setOkSerial('');
    setAlert({ tone: 'ok', text: outcome.data.message });
    beep('ok');
    await refreshSummary();
    okSerialRef.current?.focus();
  };

  const stagePair = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const label = okLabel.trim();
    const serial = okSerial.trim();
    if (!label || !serial) return;
    setAlert(null);
    setPair({ label, serial });
  };

  const savePair = async (): Promise<void> => {
    if (!pair) return;

    const key = okKey.take(['REWORK', pair.label, pair.serial].join('|'));
    setBusy(true);
    const outcome = await postJson<OkScanResult>(
      '/api/v1/ng/records/ok',
      { mode: 'REWORK', serial: pair.serial, label: pair.label, note: '', idempotencyKey: key },
      contextVersion,
    );
    setBusy(false);

    if (!outcome.ok) {
      fail(outcome.error, outcome.code);
      return;
    }

    okKey.reset();
    const label = outcome.data.unit.labelPayload ?? pair.label;
    setPair(null);
    setOkLabel('');
    setOkSerial('');
    setAlert({
      tone: 'ok',
      text: outcome.data.message,
      chip: label,
      removeLabel: outcome.data.removeLabel,
    });
    beep('ok');
    await refreshSummary();
    okLabelRef.current?.focus();
  };

  const startRework = (unit: NgUnitInfo): void => {
    setReworkRef(unitRefOf(unit));
    setReworkNote('');
    setTab('rework');
    setAlert(null);
    window.setTimeout(() => reworkInputRef.current?.focus(), 0);
  };

  const startPairFor = (unit: NgUnitInfo): void => {
    setOkMode('REWORK');
    setOkLabel(unit.labelPayload ?? '');
    setOkSerial(unit.serial ?? '');
    setPair(null);
    setTab('ok');
    setAlert(null);
  };

  const loadDetail = async (reference: string): Promise<void> => {
    setDetailBusy(true);
    try {
      const response = await fetch(`/api/v1/ng/units/${encodeURIComponent(reference)}`, { cache: 'no-store' });
      if (response.status === 404) {
        setDetail(null);
        setAlert({ tone: 'error', text: `Unit ${reference} tidak ditemukan pada catatan karantina.` });
        return;
      }
      if (!response.ok) {
        setAlert(staleAlertOf('Riwayat unit gagal dimuat.'));
        return;
      }
      setDetail((await response.json()) as NgUnitDetail);
    } catch {
      setAlert({ tone: 'error', text: 'Tidak dapat menghubungi server riwayat unit.' });
    } finally {
      setDetailBusy(false);
    }
  };

  const unitColumns: DataTableColumn<NgUnitInfo>[] = [
    {
      key: 'serial',
      label: 'Serial',
      render: (row) => <span className="mono">{row.serial ?? '—'}</span>,
    },
    {
      key: 'labelPayload',
      label: 'Label internal',
      render: (row) => <span className="mono">{row.labelPayload ?? '—'}</span>,
    },
    {
      key: 'status',
      label: 'Status',
      render: (row) => <StatusBadge tone={STATUS_TONE[row.status]}>{statusLabelOf(row)}</StatusBadge>,
    },
    {
      key: 'attempts',
      label: 'Percobaan',
      align: 'center',
      render: (row) => (
        <span className="mono">
          {row.attempts}/{row.maxAttempts}
        </span>
      ),
    },
    {
      key: 'lastEventAtUtc',
      label: 'Kejadian terakhir',
      render: (row) => formatWibDateTime(row.lastEventAtUtc),
    },
    {
      key: 'targetWorkOrderNumber',
      label: 'WO tujuan',
      render: (row) => <span className="mono">{row.targetWorkOrderNumber ?? '—'}</span>,
    },
  ];

  const steps = detail ? detailSteps(detail) : [];

  return (
    <div className="stack">
      <PageHeader
        eyebrow="Traceability • Karantina"
        title="Karantina & Rework"
        description="Catat NG dengan label internal, selesaikan rework, lalu kunci OK akhir dengan serial dan WO tujuan."
        actions={
          <StatusBadge tone="ng">{formatNumber(summary.counts.quarantine)} unit di karantina</StatusBadge>
        }
      />

      {production ? (
        <section className="context-strip" aria-label="Konteks produksi">
          <div className="context-item">
            <small>WO aktif</small>
            <b className="mono">{production.activeWorkOrderNumber ?? '—'}</b>
          </div>
          <div className="context-item">
            <small>Klien / Tujuan</small>
            <b>{production.workOrderClient ?? '—'}</b>
          </div>
          <div className="context-item">
            <small>Line / Mesin</small>
            <b>
              {production.lineCode} — {production.lineName}
            </b>
          </div>
          <div className="context-item">
            <small>Shift</small>
            <b>{production.shiftCode}</b>
          </div>
          <div className="context-item">
            <small>Model / SKU</small>
            <b>{production.skuCode}</b>
          </div>
          <div className="context-item">
            <small>Operator</small>
            <b>{production.operatorName}</b>
          </div>
        </section>
      ) : (
        <div className="scan-alert error" role="status">
          Belum ada sesi produksi aktif. Mulai sesi produksi di beranda sebelum menyimpan transaksi karantina.
        </div>
      )}

      <div className="kpis">
        <KpiCard
          label="Karantina NG"
          value={formatNumber(summary.counts.quarantine)}
          meta="Menunggu rework selesai"
          tone="ng"
        />
        <KpiCard
          label="Uji ulang"
          value={formatNumber(summary.counts.retest)}
          meta="Selesai rework, belum OK akhir"
          tone="rework"
        />
        <KpiCard
          label="Tahan QC"
          value={formatNumber(summary.counts.qcHold)}
          meta={`Batas ${summary.maxAttempts} percobaan terlampaui`}
          tone="ng"
        />
        <KpiCard
          label="OK akhir hari ini"
          value={formatNumber(summary.counts.okFinalToday)}
          meta="Lulus gerbang akhir (WIB)"
          tone="ok"
          highlight
        />
      </div>

      <div className="seg-tabs" role="tablist" aria-label="Alur karantina dan rework">
        {allowedTabs.map((item) => {
          const meta = TAB_META[item];
          return (
            <button
              key={item}
              type="button"
              role="tab"
              aria-selected={activeTab === item}
              className={cn('seg-tab', activeTab === item && 'active', activeTab === item && `tone-${meta.tone}`)}
              onClick={() => selectTab(item)}
            >
              {meta.label}
            </button>
          );
        })}
      </div>

      {alert ? (
        <div className={cn('scan-alert', alert.tone)} role="status">
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
            <span>{alert.text}</span>
            {alert.chip ? <b className="mono">{alert.chip}</b> : null}
            {alert.stale ? (
              <button type="button" className="link" onClick={() => window.location.reload()}>
                Muat ulang
              </button>
            ) : null}
          </div>
        </div>
      ) : null}

      {alert?.removeLabel && alert.chip ? (
        <Notice tone="rework">
          Transaksi OK akhir sudah tersimpan. Lepaskan stiker <b className="mono">{alert.chip}</b> dari unit sekarang;
          stiker yang sudah dilepas tidak boleh dipakai ulang.
        </Notice>
      ) : null}

      <section className="split-grid">
        <div className="stack">
          {activeTab === 'ng' ? (
            <Panel
              title={TAB_META.ng.label}
              actions={<span className="helper">{TAB_META.ng.hint}</span>}
            >
              {stations.length === 0 || !station ? (
                <Notice tone="rework">
                  Belum ada pos QC aktif untuk keluarga SKU sesi ini. Atur master stage & defect di Super Admin
                  sebelum mencatat NG.
                </Notice>
              ) : (
                <form className="scan-form" onSubmit={submitNg}>
                  <ScanCodePreview value={ngLabel} />
                  <label className="grow">
                    Label internal NG
                    <input {...scanInputProps}
                      ref={ngInputRef}
                      className="scan-input mono"
                      value={ngLabel}
                      onChange={(event) => setNgLabel(event.target.value.toUpperCase())}
                      placeholder="SCAN QR NG:000123"
                      autoComplete="off"
                      autoFocus
                    />
                  </label>
                  <label style={{ flex: '1 1 190px' }}>
                    Station
                    <select value={station.code} onChange={(event) => setStationCode(event.target.value)}>
                      {stations.map((item) => (
                        <option key={item.id} value={item.code}>
                          {item.code} — {item.name}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label style={{ flex: '1 1 210px' }}>
                    Defect
                    <select value={defect?.id ?? ''} onChange={(event) => setDefectId(event.target.value)}>
                      {stationDefects.map((item) => (
                        <option key={item.id} value={item.id}>
                          {item.groupCode} • {item.name}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label style={{ flex: '1 1 190px' }}>
                    Catatan (opsional)
                    <input
                      value={ngNote}
                      maxLength={300}
                      onChange={(event) => setNgNote(event.target.value)}
                      placeholder="≤300 karakter"
                      autoComplete="off"
                    />
                  </label>
                  <button type="submit" className="btn-primary" disabled={busy || !ngLabel.trim()}>
                    <Icon name="warning" size={14} /> Karantina NG
                  </button>
                </form>
              )}
              <div className="stat-inline" style={{ marginTop: 14 }}>
                <span>
                  Batas percobaan <b>{summary.maxAttempts}x</b>
                </span>
                <span>
                  Kejadian NG hari ini <b className="mono">{formatNumber(summary.counts.ngEventsToday)}</b>
                </span>
                <span>
                  Rework hari ini <b className="mono">{formatNumber(summary.counts.reworkToday)}</b>
                </span>
              </div>
            </Panel>
          ) : null}

          {activeTab === 'rework' ? (
            <Panel
              title={TAB_META.rework.label}
              actions={<span className="helper">{TAB_META.rework.hint}</span>}
            >
              <form className="scan-form" onSubmit={submitRework}>
                <ScanCodePreview value={reworkRef} />
                <label className="grow">
                  Label NG atau serial unit
                  <input {...scanInputProps}
                    ref={reworkInputRef}
                    className="scan-input mono"
                    value={reworkRef}
                    onChange={(event) => setReworkRef(event.target.value.toUpperCase())}
                    placeholder="SCAN NG:000123 ATAU SERIAL"
                    autoComplete="off"
                  />
                </label>
                <label style={{ flex: '1 1 260px' }}>
                  Tindakan perbaikan
                  <select value={reworkAction} onChange={(event) => setReworkAction(event.target.value)}>
                    {(reworkActions.length > 0 ? reworkActions : ['Perbaikan selesai']).map((item) => (
                      <option key={item} value={item}>
                        {item}
                      </option>
                    ))}
                  </select>
                </label>
                <label style={{ flex: '1 1 190px' }}>
                  Catatan (opsional)
                  <input
                    value={reworkNote}
                    maxLength={300}
                    onChange={(event) => setReworkNote(event.target.value)}
                    placeholder="≤300 karakter"
                    autoComplete="off"
                  />
                </label>
                <button type="submit" className="btn-primary" disabled={busy || !reworkRef.trim()}>
                  <Icon name="refresh" size={14} /> Catat rework
                </button>
              </form>
              <p className="helper" style={{ marginTop: 12 }}>
                Percobaan dihitung saat rework dicatat. Setelah batas tercapai, NG berikutnya langsung menahan unit di
                Tahan QC dan tidak boleh masuk packing.
              </p>
            </Panel>
          ) : null}

          {activeTab === 'ok' ? (
            <Panel title={TAB_META.ok.label} actions={<span className="helper">{TAB_META.ok.hint}</span>}>
              {!production?.activeWorkOrderNumber ? (
                <Notice tone="rework">
                  WO aktif belum dipilih pada sesi produksi. OK akhir ditolak sampai WO tujuan tersedia.
                </Notice>
              ) : null}

              <div className="seg-tabs" role="tablist" aria-label="Mode OK akhir" style={{ marginBottom: 14 }}>
                <button
                  type="button"
                  role="tab"
                  aria-selected={okMode === 'SERIAL'}
                  className={cn('seg-tab', okMode === 'SERIAL' && 'active')}
                  onClick={() => {
                    setOkMode('SERIAL');
                    setPair(null);
                  }}
                >
                  Serial saja
                </button>
                <button
                  type="button"
                  role="tab"
                  aria-selected={okMode === 'REWORK'}
                  className={cn('seg-tab', okMode === 'REWORK' && 'active', okMode === 'REWORK' && 'tone-rw')}
                  onClick={() => {
                    setOkMode('REWORK');
                    setPair(null);
                    setPairPreview('');
                  }}
                >
                  Pasangan label + serial
                </button>
              </div>

              {okMode === 'SERIAL' ? (
                <form className="scan-form" onSubmit={submitOkSerial}>
                  <ScanCodePreview value={okSerial} />
                  <label className="grow">
                    Serial Number
                    <input {...scanInputProps}
                      ref={okSerialRef}
                      className="scan-input mono"
                      value={okSerial}
                      onChange={(event) => setOkSerial(event.target.value.toUpperCase())}
                      placeholder="SCAN SERIAL NUMBER"
                      autoComplete="off"
                    />
                  </label>
                  <button type="submit" className="btn-primary" disabled={busy || !okSerial.trim()}>
                    <Icon name="check" size={14} /> Simpan OK
                  </button>
                </form>
              ) : (
                <>
                  <form className="scan-form" onSubmit={stagePair}>
                    <ScanCodePreview value={pairPreview} />
                    <label className="grow">
                      Label internal NG
                      <input {...scanInputProps}
                        ref={okLabelRef}
                        className="scan-input mono"
                        value={okLabel}
                        onChange={(event) => {
                          const value = event.target.value.toUpperCase();
                          setOkLabel(value);
                          setPairPreview(value);
                        }}
                        onKeyDown={(event) => {
                          if (event.key === 'Enter' || event.key === 'Tab') {
                            if (event.shiftKey || !event.currentTarget.value.trim()) return;
                            event.preventDefault();
                            okSerialRef.current?.focus();
                          }
                        }}
                        placeholder="SCAN NG:000123"
                        autoComplete="off"
                      />
                    </label>
                    <label className="grow">
                      Serial hasil rework
                      <input {...scanInputProps}
                        ref={okSerialRef}
                        className="scan-input mono"
                        value={okSerial}
                        onChange={(event) => {
                          const value = event.target.value.toUpperCase();
                          setOkSerial(value);
                          setPairPreview(value);
                        }}
                        placeholder="SCAN SERIAL NUMBER"
                        autoComplete="off"
                      />
                    </label>
                    <button type="submit" className="btn-primary" disabled={!okLabel.trim() || !okSerial.trim()}>
                      <Icon name="scan" size={14} /> Pasangkan
                    </button>
                  </form>

                  {pair ? (
                    <div style={{ marginTop: 14 }}>
                      <Notice tone="rework">
                        Periksa pasangan sebelum simpan — label <b className="mono">{pair.label}</b> menjadi serial{' '}
                        <b className="mono">{pair.serial}</b> pada WO{' '}
                        <b className="mono">{production?.activeWorkOrderNumber ?? '—'}</b>.
                      </Notice>
                      <div className="form-actions">
                        <button type="button" className="btn-primary" onClick={() => void savePair()} disabled={busy}>
                          <Icon name="check" size={14} /> Simpan OK akhir
                        </button>
                        <button type="button" onClick={() => setPair(null)} disabled={busy}>
                          <Icon name="close" size={14} /> Batal
                        </button>
                      </div>
                    </div>
                  ) : (
                    <p className="helper" style={{ marginTop: 12 }}>
                      Pasangan ditampilkan dulu untuk diperiksa. Instruksi melepas stiker hanya muncul setelah backend
                      mengonfirmasi transaksi berhasil.
                    </p>
                  )}
                </>
              )}
            </Panel>
          ) : null}

          {activeTab === 'history' ? (
            <Panel
              title={TAB_META.history.label}
              actions={<span className="helper">{TAB_META.history.hint}</span>}
            >
              <form
                className="scan-form"
                onSubmit={(event) => {
                  event.preventDefault();
                  setHistoryPage(1);
                  setHistoryQuery(historySearch.trim());
                  setHistoryTick((value) => value + 1);
                }}
              >
                <ScanCodePreview value={historySearch} />
                <label className="grow">
                  Cari serial, label, atau ID unit
                  <input {...scanInputProps}
                    ref={historyInputRef}
                    className="scan-input mono"
                    value={historySearch}
                    onChange={(event) => setHistorySearch(event.target.value)}
                    placeholder="NG:000123 / SERIAL"
                    autoComplete="off"
                  />
                </label>
                <label style={{ flex: '0 1 210px' }}>
                  Status
                  <select
                    value={historyStatus}
                    onChange={(event) => {
                      setHistoryStatus(event.target.value);
                      setHistoryPage(1);
                    }}
                  >
                    {STATUS_OPTIONS.map((item) => (
                      <option key={item.value || 'all'} value={item.value}>
                        {item.label}
                      </option>
                    ))}
                  </select>
                </label>
                <button type="submit" className="btn-primary" disabled={historyBusy}>
                  <Icon name="search" size={14} /> Cari
                </button>
              </form>

              <div style={{ marginTop: 14 }}>
                {historyBusy && !historyData ? (
                  <EmptyState>Memuat riwayat unit…</EmptyState>
                ) : (
                  <DataTable<NgUnitInfo>
                    columns={unitColumns}
                    rows={historyData?.items ?? []}
                    rowKey={(row) => row.id}
                    storageKey="ng.units"
                    title="Unit NG & OK akhir"
                    helper={`${formatNumber(historyData?.total ?? 0)} unit • halaman ${
                      historyData?.page ?? 1
                    } dari ${historyData?.totalPages ?? 1}`}
                    initialPageSize={HISTORY_PAGE_SIZE}
                    onRowClick={(row) => void loadDetail(unitRefOf(row))}
                  />
                )}
              </div>

              <div className="form-actions" style={{ marginTop: 10 }}>
                <button
                  type="button"
                  onClick={() => setHistoryPage((page) => Math.max(1, page - 1))}
                  disabled={historyBusy || (historyData?.page ?? 1) <= 1}
                >
                  ← Sebelumnya
                </button>
                <button
                  type="button"
                  onClick={() => setHistoryPage((page) => page + 1)}
                  disabled={historyBusy || (historyData?.page ?? 1) >= (historyData?.totalPages ?? 1)}
                >
                  Berikutnya →
                </button>
              </div>

              {detailBusy ? <EmptyState>Memuat jejak unit…</EmptyState> : null}

              {detail && !detailBusy ? (
                <div style={{ marginTop: 16 }}>
                  <Panel
                    title={`Jejak unit ${detail.unit.labelPayload ?? detail.unit.serial ?? detail.unit.id}`}
                    actions={
                      <button type="button" className="link" onClick={() => setDetail(null)}>
                        Tutup
                      </button>
                    }
                  >
                    <div className="context-strip" style={{ marginBottom: 14 }}>
                      <div className="context-item">
                        <small>Status</small>
                        <b>{statusLabelOf(detail.unit)}</b>
                      </div>
                      <div className="context-item">
                        <small>Percobaan</small>
                        <b className="mono">
                          {detail.unit.attempts}/{detail.unit.maxAttempts}
                        </b>
                      </div>
                      <div className="context-item">
                        <small>WO asal (NG)</small>
                        <b className="mono">{detail.unit.originWorkOrderNumber ?? '—'}</b>
                      </div>
                      <div className="context-item">
                        <small>WO tujuan (OK)</small>
                        <b className="mono">{detail.unit.targetWorkOrderNumber ?? '—'}</b>
                      </div>
                      <div className="context-item">
                        <small>OK akhir</small>
                        <b>{detail.unit.okAtUtc ? formatWibDateTime(detail.unit.okAtUtc) : '—'}</b>
                      </div>
                      <div className="context-item">
                        <small>Diverifikasi oleh</small>
                        <b>{detail.unit.okActorName ?? '—'}</b>
                      </div>
                    </div>

                    {steps.length === 0 ? (
                      <EmptyState>Belum ada kejadian tercatat untuk unit ini.</EmptyState>
                    ) : (
                      <ol className="timeline">
                        {steps.map((step, index) => (
                          <li className="timeline-step" key={`${step.at}-${index}`}>
                            <span className={cn('timeline-dot', step.tone !== 'ok' && step.tone)} />
                            <div>
                              <b>{step.label}</b>
                              <p>{step.detail}</p>
                              <span className="timeline-time mono">{formatWibDateTime(step.at)}</span>
                            </div>
                          </li>
                        ))}
                      </ol>
                    )}
                  </Panel>
                </div>
              ) : null}
            </Panel>
          ) : null}
        </div>

        <div className="stack">
          {activeTab === 'ng' ? (
            <>
              <UnitQueue
                title="Antrean karantina"
                units={summary.quarantine}
                tone="ng"
                empty="Tidak ada unit di karantina."
                actionLabel="Catat rework"
                onAction={startRework}
              />
              <Panel title="Aturan label NG">
                <p className="helper" style={{ marginTop: 0 }}>
                  Hanya QR internal hasil cetak Fase 2 yang diterima pada Scan NG. Label terikat permanen ke unit dan
                  tidak berubah saat cetak ulang; serial baru dipasangkan pada OK akhir.
                </p>
              </Panel>
            </>
          ) : null}

          {activeTab === 'rework' ? (
            <>
              <UnitQueue
                title="Antrean karantina"
                units={summary.quarantine}
                tone="ng"
                empty="Tidak ada unit menunggu rework."
                actionLabel="Isi form"
                onAction={startRework}
              />
              <UnitQueue
                title="Menunggu uji ulang"
                units={summary.retest}
                tone="rework"
                empty="Tidak ada unit di uji ulang."
              />
            </>
          ) : null}

          {activeTab === 'ok' ? (
            <>
              <UnitQueue
                title="Siap OK akhir"
                units={summary.retest}
                tone="rework"
                empty="Tidak ada unit selesai rework."
                actionLabel="Pasangkan"
                onAction={startPairFor}
              />
              <UnitQueue
                title="Tahan QC"
                units={summary.qcHold}
                tone="ng"
                empty="Tidak ada unit tertahan QC."
              />
            </>
          ) : null}

          {activeTab === 'history' ? (
            <Panel title="Ringkasan hari ini">
              <div className="stat-inline">
                <span>
                  Kejadian NG <b className="mono">{formatNumber(summary.counts.ngEventsToday)}</b>
                </span>
                <span>
                  Rework <b className="mono">{formatNumber(summary.counts.reworkToday)}</b>
                </span>
                <span>
                  OK akhir <b className="mono">{formatNumber(summary.counts.okFinalToday)}</b>
                </span>
                <span>
                  Batas percobaan <b>{summary.maxAttempts}x</b>
                </span>
              </div>
              <p className="helper" style={{ marginBottom: 0 }}>
                Klik baris unit untuk melihat urutan kejadian NG, rework per percobaan, dan pasangan serial saat OK
                akhir — bukti histori serial setelah linking.
              </p>
            </Panel>
          ) : null}
        </div>
      </section>
    </div>
  );
}
