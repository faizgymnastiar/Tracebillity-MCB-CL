'use client';

import { scanInputProps } from '@/lib/scanInput';
import { ScanCodePreview } from './ScanCodePreview';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';
import type { Tone } from '@/components/ui/SharedUi';
import type { ProductionContext } from '@/types';

type Kind = 'inner' | 'outer';

interface BoxState {
  box: { qr: string; closedAt: string | null };
  members: string[];
  count: number;
  capacity: number;
  reused: boolean;
}

interface WorkOrderSummary {
  workOrder: string;
  targetQty: number;
  ok: number;
  inner: number;
  outer: number;
}

async function packingRequest<T>(route: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`/api/v1/packing/${route}`, options);
  const result = await response.json();
  if (!response.ok) throw new Error(result.error ?? 'Permintaan gagal.');
  return result as T;
}

export function PackingWorkspace({ production, kinds }: { production: ProductionContext | null; kinds: Kind[] }) {
  const [kind, setKind] = useState(kinds[0]);
  const [qr, setQr] = useState('');
  const [member, setMember] = useState('');
  const [preview, setPreview] = useState('');
  const [state, setState] = useState<BoxState | null>(null);
  const [summary, setSummary] = useState<WorkOrderSummary | null>(null);
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const memberInput = useRef<HTMLInputElement>(null);
  const boxInput = useRef<HTMLInputElement>(null);
  const inFlight = useRef(false);

  const openBox = state?.box.qr && !state.box.closedAt;
  useEffect(() => {
    if (!busy) (openBox ? memberInput : boxInput).current?.focus();
  }, [busy, kind, openBox, production?.activeWorkOrderNumber]);

  async function update(action: 'open' | 'add' | 'close', event?: FormEvent) {
    event?.preventDefault();
    if (!production || inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setNotice('');
    try {
      const next = await packingRequest<BoxState>(`${kind}/${action}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json', 'x-trace-context-version': String(production.contextVersion) },
        body: JSON.stringify({ qr: action === 'open' ? qr : state?.box.qr, member }),
      });
      setState(next);
      setMember('');
      if (action === 'close') setQr('');
      if (next.reused) setNotice('Anggota sudah tercatat di box ini.');
      const totals = await packingRequest<WorkOrderSummary>('work-order');
      setSummary(totals);
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Packing gagal.');
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader title="Packing" eyebrow="Traceability" description="Pindai inner atau outer dan anggotanya." />
      {!production?.activeWorkOrderNumber ? <p>Mulai produksi dan pilih WO aktif di beranda.</p> : (
        <section className="scan-form">
          <p>WO {production.activeWorkOrderNumber} · {production.skuName} · {production.lineName} · {production.shiftName}</p>
          <label>Pos packing
            <select value={kind} disabled={busy} onChange={(event) => {
              setKind(event.target.value as Kind);
              setState(null);
              setQr('');
              setMember('');
              setNotice('');
            }}>
              {kinds.map((item) => <option key={item} value={item}>Scan {item}</option>)}
            </select>
          </label>
          <form onSubmit={(event) => void update('open', event)} className="scan-form">
            {!openBox && <ScanCodePreview value={preview} />}
            <label>QR {kind}<input {...scanInputProps} ref={boxInput} autoComplete="off" required value={qr} onChange={(event) => {
              setQr(event.target.value); setPreview(event.target.value); setState(null);
            }} disabled={busy} /></label>
            <button className="btn-primary" disabled={busy}>Buka box</button>
          </form>
          {state && <>
            <p><strong>{state.box.qr}</strong> · {state.count}/{state.capacity} · Sisa {state.capacity - state.count} · {state.box.closedAt ? 'Ditutup' : 'Terbuka'}</p>
            {!state.box.closedAt && <form onSubmit={(event) => void update('add', event)} className="scan-form">
              <ScanCodePreview value={preview} />
              <label>{kind === 'inner' ? 'Serial unit OK akhir' : 'QR inner tertutup'}
                <input {...scanInputProps} ref={memberInput} autoComplete="off" value={member} onChange={(event) => {
                  setMember(event.target.value); setPreview(event.target.value);
                }} required disabled={busy} />
              </label>
              <button className="btn-primary" disabled={busy}>Catat scan</button>
            </form>}
            {!state.box.closedAt && <button type="button" disabled={busy || state.count !== state.capacity} onClick={() => void update('close')}>Tutup {kind}</button>}
            <ol>{state.members.map((item) => <li key={item}>{item}</li>)}</ol>
          </>}
          {summary && <p>Target {summary.targetQty} pcs · OK akhir {summary.ok} · Masuk inner {summary.inner} · Masuk outer {summary.outer}</p>}
          <p role="status">{notice}</p>
        </section>
      )}
    </>
  );
}

interface HistoryRow {
  serial: string | null;
  label: string | null;
  sku: string;
  status: string;
  workOrder: string | null;
  originWorkOrder: string | null;
  client: string | null;
  inner: string | null;
  outer: string | null;
  time: string;
}

interface HistoryDetail {
  history: {
    unit: { serial: string | null; labelId: string | null };
    ngEvents: { id: string; stageCode: string; defectName: string; actorName: string; occurredAtUtc: string }[];
    reworkEvents: { id: string; attemptNo: number; action: string; actorName: string; performedAtUtc: string }[];
    okScan: { workOrderTargetNumber: string; occurredAtUtc: string } | null;
  };
  chain: { inner: string; outer: string | null } | null;
  destination: { workOrder: string | null; client: string | null; shippingReference: string | null } | null;
}

const timeFormat = new Intl.DateTimeFormat('id-ID', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Jakarta' });
const historyStatuses: Record<string, { label: string; tone: Tone }> = {
  OK_FINAL: { label: 'OK akhir', tone: 'ok' },
  NG_QUARANTINE: { label: 'Karantina NG', tone: 'ng' },
  REWORK_RETEST: { label: 'Uji ulang', tone: 'rework' },
  QC_HOLD: { label: 'Tahan QC', tone: 'ng' },
};

export function PackingHistory({ initialSearch }: { initialSearch: string }) {
  const searchInput = useRef<HTMLInputElement>(null);
  const [search, setSearch] = useState(initialSearch);
  const [status, setStatus] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [hasNext, setHasNext] = useState(false);
  const [rows, setRows] = useState<HistoryRow[] | null>(null);
  const [detail, setDetail] = useState<HistoryDetail | null>(null);
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => { searchInput.current?.focus(); }, []);

  const load = useCallback(async (nextPage: number, filters: string) => {
    setBusy(true);
    setNotice('');
    setDetail(null);
    try {
      const params = new URLSearchParams(filters);
      params.set('page', String(nextPage));
      const result = await packingRequest<{ items: HistoryRow[]; hasNext: boolean }>(`history?${params}`);
      setRows(result.items);
      setHasNext(result.hasNext);
      setQuery(filters);
      setPage(nextPage);
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Riwayat gagal dimuat.');
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    if (initialSearch.trim()) void load(1, new URLSearchParams({ search: initialSearch }).toString());
  }, [initialSearch, load]);

  async function openDetail(reference: string) {
    setNotice('');
    setDetail(null);
    setBusy(true);
    try {
      setDetail(await packingRequest<HistoryDetail>(`history/unit?reference=${encodeURIComponent(reference)}`));
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Riwayat unit gagal dimuat.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="unit-history">
      <PageHeader title="Riwayat Unit" eyebrow="Traceability" description="Telusuri status unit, riwayat rework, dan kemasannya." />
      <Panel title="Cari unit">
      <form className="unit-history-filters" onSubmit={(event) => {
        event.preventDefault();
        if (busy) return;
        void load(1, new URLSearchParams({ search, status, from, to }).toString());
        searchInput.current?.select();
      }}>
        <ScanCodePreview value={search} />
        <label>Serial, ID NG, box, atau WO<input {...scanInputProps} ref={searchInput} autoComplete="off" placeholder="Ketik atau pindai kode" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
        <label>Status<select value={status} onChange={(event) => setStatus(event.target.value)}>
          <option value="">Semua status</option>
          {Object.entries(historyStatuses).map(([value, item]) => <option key={value} value={value}>{item.label}</option>)}
        </select></label>
        <label>Dari tanggal<input type="date" value={from} onChange={(event) => setFrom(event.target.value)} /></label>
        <label>Sampai tanggal<input type="date" value={to} onChange={(event) => setTo(event.target.value)} /></label>
        <button className="btn-primary" disabled={busy}>{busy && !rows ? 'Memuat…' : 'Cari'}</button>
        <button type="button" disabled={busy} onClick={() => {
          setSearch(''); setStatus(''); setFrom(''); setTo('');
          void load(1, '');
        }}>Reset</button>
      </form>
      </Panel>
      {notice && <p className="scan-alert error" role="alert">{notice}</p>}
      <Panel title="Hasil pencarian" actions={rows ? <span className="helper">{rows.length} unit di halaman ini</span> : null}>
      {!rows?.length && <p className="unit-history-empty" role="status">{busy ? 'Memuat riwayat…' : rows ? 'Tidak ada unit yang cocok dengan filter.' : 'Pindai kode atau klik Cari untuk melihat riwayat.'}</p>}
      {rows && rows.length > 0 && <>
        <div className="unit-history-scroll" tabIndex={0} aria-label="Tabel riwayat unit">
        <table>
          <thead><tr><th>Serial / ID NG</th><th>Status</th><th>SKU</th><th>WO asal</th><th>WO tujuan</th><th>Klien tujuan</th><th>Inner</th><th>Outer</th><th>Waktu (WIB)</th></tr></thead>
          <tbody>{rows.map((row) => {
            const reference = row.serial ?? row.label ?? '';
            const rowStatus = historyStatuses[row.status];
            const selected = Boolean(reference && (detail?.history.unit.serial === reference || detail?.history.unit.labelId === reference));
            return <tr key={reference} data-selected={selected}>
              <td><button className="unit-history-reference" aria-pressed={selected} disabled={busy || !reference} onClick={() => void openDetail(reference)}>{reference || '—'}</button>
                {row.serial && row.label && <small>{row.label}</small>}</td>
              <td><StatusBadge tone={rowStatus?.tone ?? 'neutral'}>{rowStatus?.label ?? row.status}</StatusBadge></td><td>{row.sku}</td><td>{row.originWorkOrder ?? '—'}</td>
              <td>{row.workOrder ?? '—'}</td><td>{row.client ?? '—'}</td><td>{row.inner ?? '—'}</td><td>{row.outer ?? '—'}</td>
              <td>{timeFormat.format(new Date(row.time))}</td>
            </tr>;
          })}</tbody>
        </table>
        </div>
        <nav className="unit-history-pages" aria-label="Halaman riwayat">
          <button disabled={busy || page === 1} onClick={() => void load(page - 1, query)}>Sebelumnya</button>
          <span>Halaman {page}</span>
          <button disabled={busy || !hasNext} onClick={() => void load(page + 1, query)}>Berikutnya</button>
        </nav>
      </>}
      </Panel>
      {detail && <Panel title={`Jejak ${detail.history.unit.serial ?? detail.history.unit.labelId}`}
        actions={<button type="button" onClick={() => setDetail(null)}>Tutup detail</button>}>
        <dl className="unit-history-boxes">
          <div><dt>ID NG</dt><dd>{detail.history.unit.labelId ?? '—'}</dd></div>
          <div><dt>Inner</dt><dd>{detail.chain?.inner ?? '—'}</dd></div>
          <div><dt>Outer</dt><dd>{detail.chain?.outer ?? '—'}</dd></div>
          <div><dt>WO tujuan</dt><dd>{detail.destination?.workOrder ?? '—'}</dd></div>
          <div><dt>Klien tujuan</dt><dd>{detail.destination?.client ?? '—'}</dd></div>
          <div><dt>Referensi pengiriman</dt><dd>{detail.destination?.shippingReference ?? '—'}</dd></div>
        </dl>
        <ol className="unit-history-timeline">{[
          ...detail.history.ngEvents.map((item) => ({ id: item.id, time: item.occurredAtUtc,
            message: `NG ${item.stageCode} · ${item.defectName} · ${item.actorName}` })),
          ...detail.history.reworkEvents.map((item) => ({ id: item.id, time: item.performedAtUtc,
            message: `Rework ${item.attemptNo} · ${item.action} · ${item.actorName}` })),
          ...(detail.history.okScan ? [{ id: 'ok', time: detail.history.okScan.occurredAtUtc,
            message: `OK akhir · WO ${detail.history.okScan.workOrderTargetNumber}` }] : []),
        ].sort((a, b) => a.time.localeCompare(b.time)).map((item) =>
          <li key={item.id}><time dateTime={item.time}>{timeFormat.format(new Date(item.time))} WIB</time><strong>{item.message}</strong></li>)}</ol>
      </Panel>}
    </div>
  );
}
