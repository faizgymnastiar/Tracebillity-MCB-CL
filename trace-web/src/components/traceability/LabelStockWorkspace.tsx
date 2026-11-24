'use client';

import { scanInputProps } from '@/lib/scanInput';
import { ScanCodePreview } from './ScanCodePreview';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  DataTableColumn,
  GenerateBatchResult,
  LabelBatchDetail,
  LabelBatchInfo,
  LabelSequenceState,
  LabelStockSummary,
  NgLabelInfo,
  PagedResponse,
  PrintJobInfo,
  PrintJobResult,
} from '@/types';
import type { Tone } from '@/components/ui/SharedUi';
import { cn } from '@/lib/cn';
import { downloadTextFile } from '@/lib/download';
import { formatWibDateTime } from '@/lib/datetime';
import { formatNumber } from '@/lib/formatters';
import { DataTable } from '@/components/ui/DataTable';
import { Icon } from '@/components/ui/Icon';
import { EmptyState, KpiCard, Notice, PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';

type TabKey = 'generate' | 'batches' | 'print' | 'verify';

interface LabelStockWorkspaceProps {
  initialSummary: LabelStockSummary;
  /** Hak print.manage dari backend; layar tetap memanggil API agar penolakan terjadi di server. */
  canManage: boolean;
}

interface BatchFilters {
  search: string;
  from: string;
  to: string;
}

interface JobFilters {
  batchId: string;
  status: string;
  from: string;
  to: string;
}

const TABS: Array<{ key: TabKey; label: string; hint: string }> = [
  { key: 'generate', label: 'Generate ID', hint: 'Alokasi nomor batch dari sequence database' },
  { key: 'batches', label: 'Batch & stok', hint: 'Daftar batch, rincian ID, dan stok lintas WO' },
  { key: 'print', label: 'Cetak & riwayat', hint: 'Ekspor CSV NiceLabel dan catat hasil cetak fisik' },
  { key: 'verify', label: 'Verifikasi scan', hint: 'Bukti cetak balik: payload hasil scan → record label' },
];

const PAGE_SIZE = 25;

const JOB_STATUS_OPTIONS: Array<{ value: string; label: string }> = [
  { value: '', label: 'Semua status' },
  { value: 'EXPORTED', label: 'Menunggu pencatatan hasil' },
  { value: 'CONFIRMED', label: 'Terkonfirmasi' },
  { value: 'UNKNOWN', label: 'Tidak pasti' },
  { value: 'FAILED', label: 'Gagal' },
];

const LABEL_STATUS_OPTIONS: Array<{ value: string; label: string }> = [
  { value: '', label: 'Semua status' },
  { value: 'AVAILABLE', label: 'Stok tersedia' },
  { value: 'BOUND', label: 'Terikat unit' },
  { value: 'VOID', label: 'Dibatalkan' },
];

const CONFIRM_STATUS_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'CONFIRMED', label: 'CONFIRMED — hasil cetak sah dan terbaca' },
  { value: 'UNKNOWN', label: 'UNKNOWN — tidak pasti, perlu cetak ulang' },
  { value: 'FAILED', label: 'FAILED — cetak gagal, label tidak dipakai' },
];

async function readError(response: Response, fallback: string): Promise<string> {
  try {
    const payload = (await response.json()) as { error?: string };
    return payload.error ?? fallback;
  } catch {
    return fallback;
  }
}

/** Kunci idempotensi: satu percobaan generate = satu kunci, dipakai ulang saat kirim ulang. */
function makeIdempotencyKey(prefix: string): string {
  const random =
    globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  return `${prefix}-${random}`.toUpperCase().slice(0, 120);
}

function jobTone(status: string): Tone {
  if (status === 'CONFIRMED') return 'ok';
  if (status === 'FAILED') return 'ng';
  if (status === 'UNKNOWN') return 'rework';
  return 'neutral';
}

function labelTone(status: string): Tone {
  if (status === 'AVAILABLE') return 'ok';
  if (status === 'BOUND') return 'info';
  return 'ng';
}

function countChip(label: string, count: number): string {
  return `${label} ${formatNumber(count)}`;
}

/**
 * Layar "Generate ID" (Fase 2) — stok label QR internal NG.
 * Column identitas CSV dan kontrak payload dibuat di backend; layar ini hanya
 * memanggil /api/v1/labels/** sehingga penolakan hak akses tetap terbukti di server.
 */
export function LabelStockWorkspace({ initialSummary, canManage }: LabelStockWorkspaceProps) {
  const [tab, setTab] = useState<TabKey>(canManage ? 'generate' : 'batches');
  const [summary, setSummary] = useState<LabelStockSummary>(initialSummary);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);

  const [quantity, setQuantity] = useState('50');
  const [note, setNote] = useState('');
  const [idempotencyKey, setIdempotencyKey] = useState(() => makeIdempotencyKey('GEN'));
  const pendingPrint = useRef<{ signature: string; key: string } | null>(null);
  const [generated, setGenerated] = useState<GenerateBatchResult | null>(null);

  const [batchFilters, setBatchFilters] = useState<BatchFilters>({ search: '', from: '', to: '' });
  const [batches, setBatches] = useState<PagedResponse<LabelBatchInfo> | null>(null);
  const [batchPage, setBatchPage] = useState(1);
  const [batchBusy, setBatchBusy] = useState(false);
  const [detail, setDetail] = useState<LabelBatchDetail | null>(null);
  const [detailBusy, setDetailBusy] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);

  const [jobFilters, setJobFilters] = useState<JobFilters>({ batchId: '', status: '', from: '', to: '' });
  const [jobs, setJobs] = useState<PagedResponse<PrintJobInfo> | null>(null);
  const [jobPage, setJobPage] = useState(1);
  const [jobBusy, setJobBusy] = useState(false);
  const [activeJob, setActiveJob] = useState<PrintJobInfo | null>(null);
  const [confirmStatus, setConfirmStatus] = useState('CONFIRMED');
  const [confirmCount, setConfirmCount] = useState('');
  const [confirmNote, setConfirmNote] = useState('');

  const [reconcileSeq, setReconcileSeq] = useState('');
  const [reconcileNote, setReconcileNote] = useState('');

  const [scanValue, setScanValue] = useState('');
  const verifyInput = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (tab === 'verify') verifyInput.current?.focus();
  }, [tab]);
  const [resolved, setResolved] = useState<NgLabelInfo | null>(null);
  const [scanError, setScanError] = useState('');

  const refreshSummary = useCallback(async (): Promise<void> => {
    try {
      const response = await fetch('/api/v1/labels', { cache: 'no-store' });
      if (response.ok) setSummary((await response.json()) as LabelStockSummary);
    } catch {
      // Server label sedang mati: ringkasan terakhir tetap ditampilkan.
    }
  }, []);

  const loadBatches = useCallback(async (page: number, filters: BatchFilters): Promise<void> => {
    setBatchBusy(true);
    try {
      const query = new URLSearchParams({
        search: filters.search,
        from: filters.from,
        to: filters.to,
        page: String(page),
        pageSize: String(PAGE_SIZE),
      });
      const response = await fetch(`/api/v1/labels/batches?${query.toString()}`, { cache: 'no-store' });
      if (!response.ok) {
        setError(await readError(response, 'Daftar batch gagal dimuat.'));
        return;
      }
      setBatches((await response.json()) as PagedResponse<LabelBatchInfo>);
    } catch {
      setError('Tidak dapat menghubungi server label.');
    } finally {
      setBatchBusy(false);
    }
  }, []);

  const loadJobs = useCallback(async (page: number, filters: JobFilters): Promise<void> => {
    setJobBusy(true);
    try {
      const query = new URLSearchParams({
        batchId: filters.batchId,
        status: filters.status,
        from: filters.from,
        to: filters.to,
        page: String(page),
        pageSize: String(PAGE_SIZE),
      });
      const response = await fetch(`/api/v1/labels/print-jobs?${query.toString()}`, { cache: 'no-store' });
      if (!response.ok) {
        setError(await readError(response, 'Riwayat cetak gagal dimuat.'));
        return;
      }
      setJobs((await response.json()) as PagedResponse<PrintJobInfo>);
    } catch {
      setError('Tidak dapat menghubungi server label.');
    } finally {
      setJobBusy(false);
    }
  }, []);

  const openBatch = useCallback(async (batchId: string): Promise<void> => {
    setDetailBusy(true);
    try {
      const response = await fetch(`/api/v1/labels/batches/${encodeURIComponent(batchId)}`, { cache: 'no-store' });
      if (!response.ok) {
        setError(await readError(response, `Rincian batch ${batchId} gagal dimuat.`));
        return;
      }
      const payload = (await response.json()) as LabelBatchDetail;
      setDetail(payload);
      setSelected([]);
    } catch {
      setError('Tidak dapat menghubungi server label.');
    } finally {
      setDetailBusy(false);
    }
  }, []);

  useEffect(() => {
    void loadBatches(1, { search: '', from: '', to: '' });
    void loadJobs(1, { batchId: '', status: '', from: '', to: '' });
    void refreshSummary();
  }, [loadBatches, loadJobs, refreshSummary]);

  // -------------------------------------------------------------------------
  // Tindakan
  // -------------------------------------------------------------------------

  const submitGenerate = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const response = await fetch('/api/v1/labels/batches', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ quantity: Number.parseInt(quantity, 10), note, idempotencyKey }),
      });
      if (!response.ok) {
        setError(await readError(response, 'Generate batch label ditolak.'));
        return;
      }
      const result = (await response.json()) as GenerateBatchResult;
      setGenerated(result);
      setSummary((current) => ({ ...current, sequence: result.sequence }));
      const first = result.labels[0]?.id ?? result.batch.id;
      const last = result.labels[result.labels.length - 1]?.id ?? result.batch.id;
      setNotice(
        result.reused
          ? `Permintaan ulang dengan kunci yang sama mengembalikan batch ${result.batch.id} — tidak ada batch tambahan.`
          : `Batch ${result.batch.id} diterbitkan: ${formatNumber(result.batch.quantity)} label ${first} … ${last}.`,
      );
      if (!result.reused) setIdempotencyKey(makeIdempotencyKey('GEN'));
      await Promise.all([refreshSummary(), loadBatches(batchPage, batchFilters)]);
    } catch {
      setError('Tidak dapat menghubungi server label.');
    } finally {
      setBusy(false);
    }
  };

  const submitReconcile = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const response = await fetch('/api/v1/labels/sequence/reconcile', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          observedMaxSeq: reconcileSeq.trim() === '' ? null : Number.parseInt(reconcileSeq, 10),
          note: reconcileNote,
        }),
      });
      if (!response.ok) {
        setError(await readError(response, 'Rekonsiliasi sequence ditolak.'));
        return;
      }
      const state = (await response.json()) as LabelSequenceState;
      setSummary((current) => ({ ...current, sequence: state }));
      setNotice(
        state.consistent
          ? `Sequence aman: nilai berikutnya ${formatNumber(state.nextValue)} (batas tertinggi ${formatNumber(state.highWaterSeq)}).`
          : 'Rekonsiliasi dicatat, tetapi sequence belum konsisten. Periksa catatan penerbitan di luar file basis data.',
      );
      setReconcileSeq('');
    } catch {
      setError('Tidak dapat menghubungi server label.');
    } finally {
      setBusy(false);
    }
  };

  const toggleLabel = (labelId: string): void => {
    setSelected((current) =>
      current.includes(labelId) ? current.filter((item) => item !== labelId) : [...current, labelId],
    );
  };

  const createJob = async (batchId: string, labelIds: string[], channel = 'NICELABEL_CSV'): Promise<void> => {
    const signature = JSON.stringify([batchId, channel, [...labelIds].sort()]);
    if (pendingPrint.current?.signature !== signature) {
      pendingPrint.current = { signature, key: makeIdempotencyKey('PRT') };
    }
    const requestKey = pendingPrint.current.key;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const response = await fetch('/api/v1/labels/print-jobs', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          batchId,
          labelIds,
          channel,
          note: labelIds.length === 0 ? 'Seluruh batch' : `Subset ${labelIds.length} label`,
          idempotencyKey: requestKey,
        }),
      });
      if (!response.ok) {
        setError(await readError(response, 'Job cetak gagal dibuat.'));
        return;
      }
      const result = (await response.json()) as PrintJobResult;
      pendingPrint.current = null;
      if (result.exportCsv && result.exportFilename) {
        downloadTextFile(result.exportFilename, result.exportCsv, 'text/csv;charset=utf-8');
      }
      setActiveJob(result.job);
      setConfirmStatus('CONFIRMED');
      setConfirmCount('');
      setConfirmNote('');
      setNotice(channel === 'DIRECT_PRINTER'
        ? `Job USB ${result.job.id} dibuat. Masukkan ID ini di komponen cetak Windows untuk mengirimnya ke printer.`
        :
        `Job ${result.job.kind === 'REPRINT' ? 'cetak ulang' : 'cetak pertama'} ${result.job.quantity} label dibuat`
          + `${result.exportFilename ? ` dan ${result.exportFilename} diunduh` : ''}. `
          + 'Label belum dihitung tercetak — catat hasil cetak fisik setelah keluar dari NiceLabel.',
      );
      await Promise.all([
        refreshSummary(),
        loadJobs(jobPage, jobFilters),
        openBatch(batchId),
      ]);
    } catch {
      setError('Tidak dapat menghubungi server label.');
    } finally {
      setBusy(false);
    }
  };

  const submitConfirm = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    if (!activeJob) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const response = await fetch(
        `/api/v1/labels/print-jobs/${encodeURIComponent(activeJob.id)}/confirm`,
        {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({
            status: confirmStatus,
            printedCount: confirmCount.trim() === '' ? null : Number.parseInt(confirmCount, 10),
            note: confirmNote,
            results: [],
          }),
        },
      );
      if (!response.ok) {
        setError(await readError(response, 'Pencatatan hasil cetak ditolak.'));
        return;
      }
      const job = (await response.json()) as PrintJobInfo;
      setActiveJob(job);
      setNotice(
        job.status === 'CONFIRMED'
          ? `Hasil cetak dicatat: ${formatNumber(job.confirmedCount ?? job.quantity)} label terhitung tercetak.`
          : `Job ditandai ${job.status}; jumlah tercetak tidak bertambah. Buat job cetak ulang bila perlu.`,
      );
      await Promise.all([
        refreshSummary(),
        loadJobs(jobPage, jobFilters),
        detail ? openBatch(detail.batch.id) : Promise.resolve(),
      ]);
    } catch {
      setError('Tidak dapat menghubungi server label.');
    } finally {
      setBusy(false);
    }
  };

  const readUsbStatus = async (jobId: string): Promise<void> => {
    try {
      const response = await fetch(`/api/v1/labels/print-jobs/${encodeURIComponent(jobId)}/usb`);
      if (!response.ok) throw new Error(await readError(response, 'Status USB gagal dimuat.'));
      const { state } = (await response.json()) as { state: string };
      const messages: Record<string, string> = {
        PENDING: 'Job belum dikirim. Jalankan komponen cetak Windows menggunakan ID job.',
        CLAIMED: 'Job sudah diambil. Periksa antrean Windows sebelum membuat job cetak ulang.',
        SPOOLED: 'Job diterima spooler. Cocokkan hasil cetak fisik sebelum konfirmasi.',
        FAILED: 'Job gagal. Periksa antrean dan driver printer.',
        UNKNOWN: 'Hasil belum pasti. Periksa antrean dan label fisik sebelum cetak ulang.',
      };
      setNotice(messages[state] ?? state);
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Status USB gagal dimuat.');
    }
  };

  const submitVerify = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    if (busy) return;
    verifyInput.current?.select();
    setBusy(true);
    setScanError('');
    setResolved(null);
    try {
      const query = new URLSearchParams({ value: scanValue });
      const response = await fetch(`/api/v1/labels/resolve?${query.toString()}`, { cache: 'no-store' });
      if (!response.ok) {
        setScanError(await readError(response, 'Payload bukan label internal NG atau tidak ditemukan.'));
        return;
      }
      setResolved((await response.json()) as NgLabelInfo);
    } catch {
      setScanError('Tidak dapat menghubungi server label.');
    } finally {
      setBusy(false);
    }
  };

  const pickJob = (job: PrintJobInfo): void => {
    setActiveJob(job);
    setConfirmStatus('CONFIRMED');
    setConfirmCount('');
    setConfirmNote('');
    setError('');
    setNotice('');
    setTab('print');
  };

  // -------------------------------------------------------------------------
  // Kolom tabel
  // -------------------------------------------------------------------------

  const batchColumns: DataTableColumn<LabelBatchInfo>[] = [
    {
      key: 'id',
      label: 'Batch',
      defaultWidth: 132,
      render: (row) => (
        <button type="button" className="mono" style={{ fontWeight: 600 }} onClick={() => void openBatch(row.id)}>
          {row.id}
        </button>
      ),
    },
    {
      key: 'quantity',
      label: 'Jumlah',
      align: 'right',
      defaultWidth: 82,
      render: (row) => formatNumber(row.quantity),
    },
    {
      key: 'firstSeq',
      label: 'Rentang ID',
      defaultWidth: 190,
      render: (row) => (
        <span className="mono">
          {row.namespace}-{String(row.firstSeq).padStart(6, '0')} … {row.namespace}-{String(row.lastSeq).padStart(6, '0')}
        </span>
      ),
    },
    {
      key: 'availableCount',
      label: 'Stok',
      align: 'right',
      defaultWidth: 76,
      render: (row) => formatNumber(row.availableCount),
    },
    { key: 'boundCount', label: 'Terikat', align: 'right', defaultWidth: 84 },
    { key: 'printJobCount', label: 'Job', align: 'right', defaultWidth: 68 },
    { key: 'confirmedPrintCount', label: 'Tercetak', align: 'right', defaultWidth: 92 },
    { key: 'createdByName', label: 'Dibuat oleh', defaultWidth: 150 },
    {
      key: 'createdAtUtc',
      label: 'Waktu (WIB)',
      defaultWidth: 150,
      render: (row) => formatWibDateTime(row.createdAtUtc),
    },
    { key: 'note', label: 'Catatan', defaultWidth: 190 },
  ];

  const labelColumns: DataTableColumn<NgLabelInfo>[] = [
    {
      key: 'pick',
      label: '',
      align: 'center',
      defaultWidth: 44,
      render: (row) => (
        <input
          type="checkbox"
          checked={selected.includes(row.id)}
          onChange={() => toggleLabel(row.id)}
          aria-label={`Pilih ${row.id}`}
          disabled={!canManage}
        />
      ),
    },
    { key: 'id', label: 'ID label', defaultWidth: 128 },
    {
      key: 'payload',
      label: 'Payload QR',
      defaultWidth: 175,
      render: (row) => (
        <a href={`/labels/preview?label=${encodeURIComponent(row.id)}`} target="_blank"
          rel="noopener noreferrer" aria-label={`Lihat QR ${row.id} di tab baru`}>
          {row.payload} · Lihat QR
        </a>
      ),
    },
    {
      key: 'status',
      label: 'Status',
      defaultWidth: 110,
      render: (row) => <StatusBadge tone={labelTone(row.status)}>{row.status}</StatusBadge>,
    },
    {
      key: 'boundSerial',
      label: 'Terikat serial',
      defaultWidth: 180,
      render: (row) => row.boundSerial ?? '—',
    },
    { key: 'printedCount', label: 'Kali tercetak', align: 'right', defaultWidth: 118 },
    {
      key: 'lastPrintedAtUtc',
      label: 'Cetak terakhir (WIB)',
      defaultWidth: 165,
      render: (row) => formatWibDateTime(row.lastPrintedAtUtc),
    },
  ];

  const jobColumns: DataTableColumn<PrintJobInfo>[] = [
    {
      key: 'requestedAtUtc',
      label: 'Waktu (WIB)',
      defaultWidth: 150,
      render: (row) => formatWibDateTime(row.requestedAtUtc),
    },
    {
      key: 'batchId',
      label: 'Batch',
      defaultWidth: 124,
      render: (row) => (
        <button type="button" className="mono" onClick={() => void openBatch(row.batchId)}>
          {row.batchId}
        </button>
      ),
    },
    {
      key: 'kind',
      label: 'Jenis',
      defaultWidth: 104,
      render: (row) => (
        <StatusBadge tone={row.kind === 'REPRINT' ? 'rework' : 'info'}>{row.kind}</StatusBadge>
      ),
    },
    { key: 'quantity', label: 'Label', align: 'right', defaultWidth: 76 },
    {
      key: 'status',
      label: 'Status',
      defaultWidth: 128,
      render: (row) => <StatusBadge tone={jobTone(row.status)}>{row.status}</StatusBadge>,
    },
    {
      key: 'confirmedCount',
      label: 'Tercatat',
      align: 'right',
      defaultWidth: 90,
      render: (row) => row.confirmedCount ?? '—',
    },
    { key: 'channel', label: 'Saluran', defaultWidth: 140, optional: true },
    { key: 'requestedByName', label: 'Operator', defaultWidth: 150 },
    {
      key: 'exportFilename',
      label: 'Berkas CSV',
      defaultWidth: 300,
      render: (row) => <span className="mono">{row.exportFilename ?? '—'}</span>,
    },
    {
      key: 'actions',
      label: 'Aksi',
      defaultWidth: 190,
      render: (row) => (
        <div className="panel-actions">
          <a
            href={`/api/v1/labels/print-jobs/${encodeURIComponent(row.id)}/export`}
            className="hero-button ghost"
            style={{ padding: '4px 10px', borderColor: '#cbd5e1', color: '#161b41' }}
          >
            <Icon name="download" size={13} /> CSV
          </a>
          <button type="button" onClick={() => pickJob(row)}>
            {row.status === 'CONFIRMED' ? 'Buka rincian' : 'Catat hasil'}
          </button>
        </div>
      ),
    },
  ];

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  const sequence = summary.sequence;
  const batchTotalPages = batches?.totalPages ?? 1;
  const jobTotalPages = jobs?.totalPages ?? 1;

  return (
    <>
      <PageHeader
        eyebrow="TRACEABILITY • STOK LABEL QR NG"
        title="Generate ID & Pencetakan Label"
        description={
          `Stok label internal lintas WO: ${formatNumber(summary.availableStock)} tersedia dari `
          + `${formatNumber(summary.totalLabels)} ID terbit. Satu ID hanya boleh terikat satu unit.`
        }
        actions={
          <div className="panel-actions">
            <StatusBadge tone={sequence.consistent ? 'ok' : 'ng'}>
              Sequence {sequence.consistent ? 'AMAN' : 'PERLU REKONSILIASI'}
            </StatusBadge>
            <button type="button" onClick={() => void Promise.all([
              refreshSummary(),
              loadBatches(batchPage, batchFilters),
              loadJobs(jobPage, jobFilters),
            ])} disabled={batchBusy || jobBusy}>
              <Icon name="refresh" size={14} /> Muat ulang
            </button>
          </div>
        }
      />

      {error ? <div className="scan-alert error" role="alert">{error}</div> : null}
      {notice ? <div className="scan-alert ok" role="status">{notice}</div> : null}
      {!canManage ? (
        <Notice tone="rework">
          Akun login ini tidak memiliki hak <b>print.manage</b>, jadi generate batch, ekspor CSV, dan pencatatan
          hasil cetak ditolak server. Layar ini hanya menampilkan riwayat.
        </Notice>
      ) : null}
      {!sequence.consistent ? (
        <Notice tone="error">
          Catatan penerbitan mengenal ID lebih tinggi daripada sequence basis data
          (batas tertinggi {formatNumber(sequence.highWaterSeq)}, nilai berikutnya {formatNumber(sequence.nextValue)}).
          Generate ditolak sampai rekonsiliasi selesai — lihat tab Generate ID.
        </Notice>
      ) : null}

      <div className="seg-tabs" role="tablist" aria-label="Bagian stok label">
        {TABS.filter((item) => canManage || item.key !== 'generate').map((item) => (
          <button
            key={item.key}
            type="button"
            role="tab"
            aria-selected={tab === item.key}
            className={cn('seg-tab', tab === item.key && 'active')}
            title={item.hint}
            onClick={() => setTab(item.key)}
          >
            {item.label}
          </button>
        ))}
      </div>

      <div className="kpis" style={{ marginBottom: 18 }}>
        <KpiCard label="Stok tersedia" value={formatNumber(summary.availableStock)} meta="Label belum terikat unit" tone="ok" highlight />
        <KpiCard label="Terikat unit" value={formatNumber(summary.boundStock)} meta="Dipakai Fase 3" tone="info" />
        <KpiCard label="Batch terbit" value={formatNumber(summary.totalBatches)} meta={`${formatNumber(summary.totalLabels)} ID total`} />
        <KpiCard
          label="Menunggu dicatat"
          value={formatNumber(summary.pendingConfirmationJobs)}
          meta="Job diekspor, hasil belum dicatat"
          tone={summary.pendingConfirmationJobs > 0 ? 'rework' : 'neutral'}
        />
      </div>

      {tab === 'generate' ? (
        <section className="split-grid">
          <div className="stack">
            <Panel
              title="Generate batch ID label"
              actions={<StatusBadge tone="neutral">namespace {sequence.name}</StatusBadge>}
            >
              <p className="helper" style={{ marginTop: 0 }}>
                Nomor ID dialokasikan dari sequence database sehingga permintaan paralel tidak pernah
                menghasilkan ID/payload yang sama. Batch dan seluruh labelnya disimpan dalam satu transaksi:
                bila proses gagal di tengah, tidak ada batch sebagian (lompatan nomor diperbolehkan).
              </p>
              <form onSubmit={submitGenerate} className="form-grid">
                <label>
                  Jumlah label
                  <input
                    type="number"
                    min={1}
                    className="mono"
                    value={quantity}
                    onChange={(event) => setQuantity(event.target.value)}
                    required
                  />
                  <span className="helper">
                    Wajib lebih dari 0; batas per permintaan diukur dari Trace:Labels:MaxPerRequest dan tidak
                    membatasi jumlah label sepanjang umur sistem.
                  </span>
                </label>
                <label>
                  Catatan batch
                  <input
                    value={note}
                    onChange={(event) => setNote(event.target.value)}
                    placeholder="mis. stok awal lini NG, maksimal 200 karakter"
                  />
                </label>
                <label style={{ gridColumn: '1 / -1' }}>
                  Kunci idempotensi
                  <input
                    className="mono"
                    value={idempotencyKey}
                    onChange={(event) => setIdempotencyKey(event.target.value)}
                    required
                  />
                  <span className="helper">
                    Kunci yang sama + isi yang sama mengembalikan batch yang sama (aman saat respons hilang lalu
                    dikirim ulang). Kunci yang sama + isi berbeda ditolak.
                  </span>
                </label>
                <div className="form-actions" style={{ gridColumn: '1 / -1' }}>
                  <button type="submit" className="btn-primary" disabled={busy || !canManage}>
                    <Icon name="check" size={14} /> Generate batch
                  </button>
                  <button type="button" onClick={() => setIdempotencyKey(makeIdempotencyKey('GEN'))} disabled={busy}>
                    Kunci baru
                  </button>
                </div>
              </form>
            </Panel>

            {generated ? (
              <Panel
                title={`Hasil permintaan — batch ${generated.batch.id}`}
                actions={
                  <div className="panel-actions">
                    <StatusBadge tone={generated.reused ? 'rework' : 'ok'}>
                      {generated.reused ? 'BATCH LAMA DIPAKAI KEMBALI' : 'BATCH BARU'}
                    </StatusBadge>
                    <button type="button" onClick={() => void openBatch(generated.batch.id)}>
                      Buka rincian
                    </button>
                  </div>
                }
              >
                <div className="table-wrap" style={{ maxHeight: 'none' }}>
                  <table className="erp-adjustable-grid">
                    <tbody>
                      <tr>
                        <th>Rentang ID</th>
                        <td className="mono">
                          {generated.batch.namespace}-{String(generated.batch.firstSeq).padStart(6, '0')} …{' '}
                          {generated.batch.namespace}-{String(generated.batch.lastSeq).padStart(6, '0')}
                        </td>
                      </tr>
                      <tr>
                        <th>Payload pertama / terakhir</th>
                        <td className="mono">
                          {generated.labels[0]?.payload ?? '—'} / {generated.labels[generated.labels.length - 1]?.payload ?? '—'}
                        </td>
                      </tr>
                      <tr>
                        <th>Diterbitkan oleh</th>
                        <td>
                          {generated.batch.createdByName} • {formatWibDateTime(generated.batch.createdAtUtc)}
                        </td>
                      </tr>
                      <tr>
                        <th>Sequence berikutnya</th>
                        <td className="mono">{formatNumber(generated.sequence.nextValue)}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <p className="helper">
                  Payload final tersimpan permanen, jadi cetak ulang menghasilkan QR yang sama persis.
                </p>
              </Panel>
            ) : null}
          </div>

          <div className="stack">
            <Panel
              title="Proteksi sequence saat pemulihan backup"
              actions={
                <StatusBadge tone={sequence.consistent ? 'ok' : 'ng'}>
                  {sequence.consistent ? 'KONSISTEN' : 'TERTINGGAL'}
                </StatusBadge>
              }
            >
              <div className="table-wrap" style={{ maxHeight: 'none' }}>
                <table className="erp-adjustable-grid">
                  <tbody>
                    <tr>
                      <th>Nilai berikutnya (DB)</th>
                      <td className="mono">{formatNumber(sequence.nextValue)}</td>
                    </tr>
                    <tr>
                      <th>ID tertinggi di tabel label</th>
                      <td className="mono">{formatNumber(sequence.maxLabelSeq)}</td>
                    </tr>
                    <tr>
                      <th>Batas di catatan penerbitan</th>
                      <td className="mono">{formatNumber(sequence.ledgerMaxSeq)}</td>
                    </tr>
                    <tr>
                      <th>Titik aman tertinggi</th>
                      <td className="mono">{formatNumber(sequence.highWaterSeq)}</td>
                    </tr>
                    <tr>
                      <th>Lokasi catatan penerbitan</th>
                      <td className="mono" style={{ wordBreak: 'break-all' }}>{sequence.ledgerPath}</td>
                    </tr>
                    {sequence.ledgerError ? (
                      <tr>
                        <th>Catatan penerbitan</th>
                        <td style={{ color: '#a30f2d' }}>{sequence.ledgerError}</td>
                      </tr>
                    ) : null}
                  </tbody>
                </table>
              </div>
              <p className="helper">
                Catatan penerbitan ditulis di luar file basis data sebelum transaksi commit. Bila backup yang
                dipulihkan tertinggal dari label yang masih beredar, generate berhenti dan operator diminta
                melaporkan ID tertinggi yang masih beredar.
              </p>
              <form onSubmit={submitReconcile} className="form-grid">
                <label>
                  ID tertinggi yang masih beredar (opsional)
                  <input
                    type="number"
                    min={1}
                    className="mono"
                    value={reconcileSeq}
                    onChange={(event) => setReconcileSeq(event.target.value)}
                    placeholder="kosongkan untuk memakai catatan penerbitan"
                  />
                </label>
                <label>
                  Dasar rekonsiliasi
                  <input
                    value={reconcileNote}
                    onChange={(event) => setReconcileNote(event.target.value)}
                    placeholder="mis. laporan stok 12-11-2026, backup 09-11-2026"
                  />
                </label>
                <div className="form-actions" style={{ gridColumn: '1 / -1' }}>
                  <button type="submit" className="btn-primary" disabled={busy || !canManage}>
                    <Icon name="shield" size={14} /> Rekonsiliasi sequence
                  </button>
                </div>
              </form>
            </Panel>
          </div>
        </section>
      ) : null}

      {tab === 'batches' ? (
        <div className="stack">
          <section className="filters">
            <form
              onSubmit={(event) => {
                event.preventDefault();
                setBatchPage(1);
                void loadBatches(1, batchFilters);
              }}
              style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}
            >
              <label style={{ flex: '1 1 260px' }}>
                Cari batch / catatan
                <input
                  value={batchFilters.search}
                  onChange={(event) => setBatchFilters((current) => ({ ...current, search: event.target.value }))}
                  placeholder="ID batch atau isi catatan"
                  autoComplete="off"
                />
              </label>
              <label style={{ flex: '0 1 170px' }}>
                Generate dari (WIB)
                <input
                  type="date"
                  value={batchFilters.from}
                  onChange={(event) => setBatchFilters((current) => ({ ...current, from: event.target.value }))}
                />
              </label>
              <label style={{ flex: '0 1 170px' }}>
                Generate sampai (WIB)
                <input
                  type="date"
                  value={batchFilters.to}
                  onChange={(event) => setBatchFilters((current) => ({ ...current, to: event.target.value }))}
                />
              </label>
              <button type="submit" className="btn-primary" disabled={batchBusy}>
                <Icon name="search" size={14} /> Terapkan filter
              </button>
              <button
                type="button"
                disabled={batchBusy}
                onClick={() => {
                  const empty = { search: '', from: '', to: '' };
                  setBatchFilters(empty);
                  setBatchPage(1);
                  void loadBatches(1, empty);
                }}
              >
                Reset
              </button>
            </form>
          </section>

          <DataTable
            columns={batchColumns}
            rows={batches?.items ?? []}
            rowKey={(row) => row.id}
            storageKey="label-batches"
            title="Daftar batch label"
            helper={
              detailBusy
                ? 'Memuat rincian batch…'
                : `${formatNumber(batches?.items.length ?? 0)} dari ${formatNumber(batches?.total ?? 0)} batch pada halaman ini.`
            }
            pageSizeOptions={[25, 50, 100]}
            initialPageSize={25}
            toolbar={
              <div className="panel-actions">
                <span className="helper">{batchBusy ? 'Memuat…' : `Halaman ${formatNumber(batchPage)} / ${formatNumber(batchTotalPages)}`}</span>
                <button type="button" disabled={batchBusy || batchPage <= 1} onClick={() => {
                  const next = Math.max(1, batchPage - 1);
                  setBatchPage(next);
                  void loadBatches(next, batchFilters);
                }}>
                  Sebelumnya
                </button>
                <button type="button" disabled={batchBusy || batchPage >= batchTotalPages} onClick={() => {
                  const next = Math.min(batchTotalPages, batchPage + 1);
                  setBatchPage(next);
                  void loadBatches(next, batchFilters);
                }}>
                  Berikutnya
                </button>
              </div>
            }
          />
          {(batches?.items.length ?? 0) === 0 && !batchBusy ? (
            <EmptyState>Belum ada batch label. Gunakan tab Generate ID untuk menerbitkan stok pertama.</EmptyState>
          ) : null}

          {detail ? (
            <Panel
              title={`Rincian batch ${detail.batch.id}`}
              actions={
                <div className="panel-actions">
                  <StatusBadge tone="ok">{countChip('stok', detail.batch.availableCount)}</StatusBadge>
                  <StatusBadge tone="info">{countChip('terikat', detail.batch.boundCount)}</StatusBadge>
                  <StatusBadge tone="neutral">{detail.printJobs.length} job cetak</StatusBadge>
                  <a className="label-preview-action" href={`/labels/preview?batch=${encodeURIComponent(detail.batch.id)}`}
                    target="_blank" rel="noopener noreferrer">Pratinjau batch</a>
                  {canManage && <a className="label-preview-action label-preview-action-primary"
                      href={`/labels/preview?batch=${encodeURIComponent(detail.batch.id)}&print=1${selected.length > 0 && selected.length !== detail.batch.quantity ? `&selected=${selected.map(encodeURIComponent).join(',')}` : ''}`}
                    target="_blank" rel="noopener noreferrer"
                    onClick={(event) => {
                      if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                      const preview = window.open(event.currentTarget.href, 'ngLabelPrintPreview', 'popup,width=1020,height=760');
                      if (preview) {
                        event.preventDefault();
                        preview.focus();
                      }
                    }}>
                    <Icon name="print" size={14} /> {selected.length ? `Cetak USB (${selected.length} label)` : 'Cetak USB (seluruh batch)'}
                  </a>}
                  <button type="button" onClick={() => setDetail(null)}>Tutup</button>
                </div>
              }
            >
              <p className="helper" style={{ marginTop: 0 }}>
                Dibuat oleh {detail.batch.createdByName} • {formatWibDateTime(detail.batch.createdAtUtc)}
                {detail.batch.note ? ` • ${detail.batch.note}` : ''}
              </p>
              <DataTable
                columns={labelColumns}
                rows={detail.labels}
                rowKey={(row) => row.id}
                storageKey="label-detail"
                title="ID & payload label"
                helper="Centang label untuk mencetak sebagian; kosongkan seluruh centang untuk mencetak satu batch penuh."
                pageSizeOptions={[25, 50, 100, 250]}
                initialPageSize={50}
                toolbar={
                  <div className="panel-actions">
                    <span className="helper">{selected.length} label terpilih</span>
                    <button
                      type="button"
                      disabled={busy || !canManage}
                      onClick={() => void createJob(detail.batch.id, selected)}
                    >
                      <Icon name="print" size={14} /> Buat job cetak CSV
                    </button>
                    {summary.usbPrintEnabled && <button type="button" disabled={busy || !canManage}
                      onClick={() => void createJob(detail.batch.id, selected, 'DIRECT_PRINTER')}>
                      <Icon name="print" size={14} /> Job USB tanpa dialog
                    </button>}
                    <button type="button" disabled={busy} onClick={() => setSelected(detail.labels.map((row) => row.id))}>
                      Pilih semua
                    </button>
                    <button type="button" disabled={busy} onClick={() => setSelected([])}>
                      Kosongkan
                    </button>
                  </div>
                }
              />
              {detail.printJobs.length === 0 ? (
                <EmptyState>Batch ini belum pernah dibuat job cetaknya.</EmptyState>
              ) : (
                <div className="list">
                  {detail.printJobs.map((job) => (
                    <div className="list-row" key={job.id}>
                      <div>
                        <b>{job.kind === 'REPRINT' ? 'Cetak ulang' : 'Cetak pertama'} — {job.quantity} label</b>
                        <p className="mono">{job.exportFilename ?? 'tanpa berkas'}</p>
                        <span className="helper">
                          {job.requestedByName} • {formatWibDateTime(job.requestedAtUtc)}
                          {job.confirmedAtUtc ? ` • dicatat ${formatWibDateTime(job.confirmedAtUtc)}` : ''}
                        </span>
                      </div>
                      <div className="list-tail">
                        <StatusBadge tone={jobTone(job.status)}>{job.status}</StatusBadge>
                        <a
                          href={`/api/v1/labels/print-jobs/${encodeURIComponent(job.id)}/export`}
                          className="hero-button ghost"
                          style={{ padding: '4px 10px', borderColor: '#cbd5e1', color: '#161b41' }}
                        >
                          <Icon name="download" size={13} /> CSV
                        </a>
                        <button type="button" onClick={() => pickJob(job)}>
                          {job.status === 'CONFIRMED' ? 'Buka rincian' : 'Catat hasil'}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </Panel>
          ) : null}
        </div>
      ) : null}

      {tab === 'print' ? (
        <div className="stack">
          <section className="filters">
            <form
              onSubmit={(event) => {
                event.preventDefault();
                setJobPage(1);
                void loadJobs(1, jobFilters);
              }}
              style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}
            >
              <label style={{ flex: '0 1 200px' }}>
                Batch
                <input
                  className="mono"
                  value={jobFilters.batchId}
                  onChange={(event) => setJobFilters((current) => ({ ...current, batchId: event.target.value.toUpperCase() }))}
                  placeholder="LB-000123"
                  autoComplete="off"
                />
              </label>
              <label style={{ flex: '0 1 230px' }}>
                Status job
                <select
                  value={jobFilters.status}
                  onChange={(event) => setJobFilters((current) => ({ ...current, status: event.target.value }))}
                >
                  {JOB_STATUS_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </label>
              <label style={{ flex: '0 1 170px' }}>
                Tanggal dari (WIB)
                <input
                  type="date"
                  value={jobFilters.from}
                  onChange={(event) => setJobFilters((current) => ({ ...current, from: event.target.value }))}
                />
              </label>
              <label style={{ flex: '0 1 170px' }}>
                Tanggal sampai (WIB)
                <input
                  type="date"
                  value={jobFilters.to}
                  onChange={(event) => setJobFilters((current) => ({ ...current, to: event.target.value }))}
                />
              </label>
              <button type="submit" className="btn-primary" disabled={jobBusy}>
                <Icon name="search" size={14} /> Terapkan filter
              </button>
              <button
                type="button"
                disabled={jobBusy}
                onClick={() => {
                  const empty = { batchId: '', status: '', from: '', to: '' };
                  setJobFilters(empty);
                  setJobPage(1);
                  void loadJobs(1, empty);
                }}
              >
                Reset
              </button>
            </form>
            <p className="helper" style={{ marginTop: 8 }}>
              Ekspor CSV ke template NiceLabel tidak menandai label tercetak. Penghitung baru bertambah setelah
              operator mencatat hasil cetak fisik pada form di bawah.
            </p>
          </section>

          <DataTable
            columns={jobColumns}
            rows={jobs?.items ?? []}
            rowKey={(row) => row.id}
            storageKey="label-print-jobs"
            title="Riwayat job cetak"
            helper={`${formatNumber(jobs?.items.length ?? 0)} dari ${formatNumber(jobs?.total ?? 0)} job pada halaman ini.`}
            pageSizeOptions={[25, 50, 100]}
            initialPageSize={25}
            toolbar={
              <div className="panel-actions">
                <span className="helper">{jobBusy ? 'Memuat…' : `Halaman ${formatNumber(jobPage)} / ${formatNumber(jobTotalPages)}`}</span>
                <button type="button" disabled={jobBusy || jobPage <= 1} onClick={() => {
                  const next = Math.max(1, jobPage - 1);
                  setJobPage(next);
                  void loadJobs(next, jobFilters);
                }}>
                  Sebelumnya
                </button>
                <button type="button" disabled={jobBusy || jobPage >= jobTotalPages} onClick={() => {
                  const next = Math.min(jobTotalPages, jobPage + 1);
                  setJobPage(next);
                  void loadJobs(next, jobFilters);
                }}>
                  Berikutnya
                </button>
              </div>
            }
          />
          {(jobs?.items.length ?? 0) === 0 && !jobBusy ? (
            <EmptyState>Belum ada job cetak untuk filter ini.</EmptyState>
          ) : null}

          {activeJob ? (
            <Panel
              title={`Job ${activeJob.id.slice(0, 12)} — batch ${activeJob.batchId}`}
              actions={
                <div className="panel-actions">
                  <StatusBadge tone={activeJob.kind === 'REPRINT' ? 'rework' : 'info'}>{activeJob.kind}</StatusBadge>
                  <StatusBadge tone={jobTone(activeJob.status)}>{activeJob.status}</StatusBadge>
                  <span className="helper">{activeJob.quantity} label</span>
                </div>
              }
            >
              <p className="helper" style={{ marginTop: 0 }}>
                Diminta oleh {activeJob.requestedByName} • {formatWibDateTime(activeJob.requestedAtUtc)}
                {activeJob.confirmedAtUtc ? ` • dicatat ${formatWibDateTime(activeJob.confirmedAtUtc)}` : ''}
                {activeJob.exportFilename ? ` • berkas ${activeJob.exportFilename}` : ''}
              </p>
              <div className="chip-row" style={{ marginBottom: 12 }}>
                {activeJob.labelIds.slice(0, 12).map((labelId) => (
                  <span key={labelId} className="chip">
                    {labelId}
                  </span>
                ))}
                {activeJob.labelIds.length > 12 ? (
                  <span className="helper">+{activeJob.labelIds.length - 12} label lainnya</span>
                ) : null}
              </div>
              {activeJob.channel === 'DIRECT_PRINTER' && <p>
                ID job USB <code>{activeJob.id}</code>{' '}
                <button type="button" onClick={() => void readUsbStatus(activeJob.id)}>Cek status USB</button>
              </p>}
              {canManage && <form onSubmit={submitConfirm} className="form-grid">
                <label>
                  Status hasil cetak
                  <select
                    value={confirmStatus}
                    onChange={(event) => setConfirmStatus(event.target.value)}
                    disabled={busy || !canManage}
                  >
                    {CONFIRM_STATUS_OPTIONS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                  <span className="helper">
                    CONFIRMED tanpa jumlah berarti seluruh {activeJob.quantity} label terhitung tercetak.
                  </span>
                </label>
                <label>
                  Jumlah benar tercetak
                  <input
                    type="number"
                    min={0}
                    max={activeJob.quantity}
                    className="mono"
                    value={confirmCount}
                    onChange={(event) => setConfirmCount(event.target.value)}
                    placeholder="kosongkan bila semua sah"
                    disabled={busy || !canManage || confirmStatus !== 'CONFIRMED'}
                  />
                  <span className="helper">Isi hanya bila sebagian label hilang/rusak saat pemeriksaan fisik.</span>
                </label>
                <label style={{ gridColumn: '1 / -1' }}>
                  Catatan pemeriksaan
                  <input
                    value={confirmNote}
                    onChange={(event) => setConfirmNote(event.target.value)}
                    placeholder="mis. 2 label tidak terbaca scanner, dicetak ulang"
                    disabled={busy || !canManage}
                  />
                </label>
                <div className="form-actions" style={{ gridColumn: '1 / -1' }}>
                  <button type="submit" className="btn-primary" disabled={busy || !canManage || activeJob.status === 'CONFIRMED'}>
                    <Icon name="check" size={14} /> Simpan catatan hasil cetak
                  </button>
                  <a
                    href={`/api/v1/labels/print-jobs/${encodeURIComponent(activeJob.id)}/export`}
                    className="hero-button ghost"
                    style={{ padding: '6px 12px', borderColor: '#cbd5e1', color: '#161b41' }}
                  >
                    <Icon name="download" size={14} /> Unduh ulang CSV
                  </a>
                  <button
                    type="button"
                    disabled={busy || !canManage}
                    onClick={() => void createJob(activeJob.batchId, activeJob.labelIds,
                      summary.usbPrintEnabled ? activeJob.channel : 'NICELABEL_CSV')}
                  >
                    <Icon name="print" size={14} /> {activeJob.channel === 'DIRECT_PRINTER' && !summary.usbPrintEnabled
                      ? 'Cetak ulang via CSV' : 'Cetak ulang (job baru, ID sama)'}
                  </button>
                </div>
              </form>}
              {activeJob.note ? <p className="helper">{activeJob.note}</p> : null}
            </Panel>
          ) : null}
        </div>
      ) : null}

      {tab === 'verify' ? (
        <section className="split-grid">
          <Panel
            title="Verifikasi payload hasil scan"
            actions={<span className="helper">{`kontrak scanner: ^NG:\\d{6,}$`}</span>}
          >
            <p className="helper" style={{ marginTop: 0 }}>
              Tempel isi QR yang dibaca scanner setelah label dicetak lewat template NiceLabel. Bukti cetak balik
              sah bila payload sama persis dengan record dan dapat dibaca sebagai ID internal.
            </p>
            <form onSubmit={submitVerify} className="form-grid">
              <ScanCodePreview value={scanValue} />
              <label style={{ gridColumn: '1 / -1' }}>
                Payload atau ID label
                <input {...scanInputProps}
                  className="mono"
                  ref={verifyInput}
                  value={scanValue}
                  onChange={(event) => setScanValue(event.target.value)}
                  placeholder="NG:000123 atau NG-000123"
                  required
                />
              </label>
              <div className="form-actions" style={{ gridColumn: '1 / -1' }}>
                <button type="submit" className="btn-primary" disabled={busy}>
                  <Icon name="scan" size={14} /> Cocokkan dengan basis data
                </button>
                <button type="button" disabled={busy} onClick={() => {
                  setScanValue('');
                  setResolved(null);
                  setScanError('');
                }}>
                  Bersihkan
                </button>
              </div>
            </form>
            {scanError ? <div className="scan-alert error" role="alert">{scanError}</div> : null}
          </Panel>

          <Panel
            title="Hasil pencocokan"
            actions={resolved ? <StatusBadge tone={labelTone(resolved.status)}>{resolved.status}</StatusBadge> : null}
          >
            {!resolved ? (
              <EmptyState>Belum ada payload yang dicocokkan.</EmptyState>
            ) : (
              <div className="table-wrap" style={{ maxHeight: 'none' }}>
                <table className="erp-adjustable-grid">
                  <tbody>
                    <tr>
                      <th>ID internal</th>
                      <td className="mono">{resolved.id}</td>
                    </tr>
                    <tr>
                      <th>Payload tersimpan</th>
                      <td className="mono">{resolved.payload}</td>
                    </tr>
                    <tr>
                      <th>Sama dengan hasil scan</th>
                      <td className="mono" style={{ color: resolved.payload === scanValue.trim() ? '#0b6b46' : '#a30f2d' }}>
                        {resolved.payload === scanValue.trim() ? 'YA' : 'BEDA'}
                      </td>
                    </tr>
                    <tr>
                      <th>Batch</th>
                      <td className="mono">{resolved.batchId}</td>
                    </tr>
                    <tr>
                      <th>Terikat unit</th>
                      <td className="mono">{resolved.boundSerial ?? 'belum terikat'}</td>
                    </tr>
                    <tr>
                      <th>Sudah tercetak</th>
                      <td className="mono">
                        {formatNumber(resolved.printedCount)} × • {formatWibDateTime(resolved.lastPrintedAtUtc)}
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            )}
          </Panel>
        </section>
      ) : null}
    </>
  );
}
