import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import { callTraceApi } from '@/server/trace-backend';
import { getTraceSession } from '@/server/session';
import { PrintLabelButton } from './PrintLabelButton';
import type { LabelBatchDetail, NgLabelInfo } from '@/types';

export const dynamic = 'force-dynamic';

const pageSize = 24;

export default async function LabelPreviewPage({
  searchParams,
}: {
  searchParams: Promise<{ batch?: string; label?: string; page?: string; print?: string; selected?: string | string[] }>;
}) {
  const [{ batch, label, page, print, selected }, session] = await Promise.all([searchParams, getTraceSession()]);
  const sessionToken = session.token;
  if (!sessionToken) redirect('/login');
  const printMode = Boolean(batch && print === '1');
  if (printMode && !session.remote?.permissions.includes('print.manage')) {
    return <main className="ng-label-preview"><h1>Akses cetak ditolak</h1></main>;
  }
  if (!batch && !label) notFound();

  const response = batch
    ? await callTraceApi(`/api/v1/labels/batches/${encodeURIComponent(batch)}`, {
      sessionToken, query: { labelLimit: 0 },
    })
    : await callTraceApi('/api/v1/labels/resolve', { sessionToken, query: { value: label } });
  if (response.status === 401) redirect('/login');
  if (response.status === 403) return <main className="ng-label-preview"><h1>Akses pratinjau ditolak</h1></main>;
  if (response.status === 404) notFound();
  if (!response.ok) return <main className="ng-label-preview"><h1>Pratinjau gagal dimuat</h1></main>;

  const labels = batch
    ? ((await response.json()) as LabelBatchDetail).labels
    : [(await response.json()) as NgLabelInfo];
  const selectedIds = printMode && selected !== undefined
    ? new Set(typeof selected === 'string' ? selected.split(',') : [])
    : null;
  const printLabels = selectedIds ? labels.filter((item) => selectedIds.has(item.id)) : labels;
  if (selectedIds && (!printLabels.length || printLabels.length !== selectedIds.size)) {
    return <main className="ng-label-preview"><h1>Pilihan label tidak valid</h1>
      <p>Kembali ke rincian batch dan pilih ulang label yang ingin dicetak.</p></main>;
  }
  const totalPages = Math.max(1, Math.ceil(labels.length / pageSize));
  const currentPage = Math.min(totalPages, Math.max(1, Number.parseInt(page ?? '', 10) || 1));
  const pageLink = (number: number) => `/labels/preview?batch=${encodeURIComponent(batch ?? '')}&page=${number}`;
  const visibleLabels = printMode ? printLabels : labels.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const rows = Array.from({ length: Math.ceil(visibleLabels.length / 4) }, (_, index) =>
    visibleLabels.slice(index * 4, index * 4 + 4));

  return (
    <main className={printMode ? 'ng-label-preview ng-label-print' : 'ng-label-preview'}>
      {printMode && <style>{'@media print { @page { size: 95mm 13mm; margin: 0; } }'}</style>}
      <header>
        <Link href="/modules/traceability/labels">Generate ID</Link>
        <h1>{batch ? `Pratinjau batch ${batch}` : `Pratinjau ${labels[0].id}`}</h1>
        <p>Media 95 mm · Label 20 × 10 mm · 4 kolom · Celah 3 mm horizontal dan vertikal</p>
        {printMode && <>
          <p>{printLabels.length} label. Layout browser memakai 95 × 13 mm per baris (10 mm stiker + 3 mm celah). Pilih antrean ARGOX, skala 100%, dan matikan header serta footer.</p>
          {printLabels.length > 0 && <PrintLabelButton expectedCount={printLabels.length} />}
        </>}
      </header>
      <label className="ng-label-scale-control">
        <input type="checkbox" id="ng-label-scale" /> Ukuran asli (100%)
      </label>
      <div className="ng-label-preview-scroll">
        {rows.map((row) => <ol className="ng-label-preview-sheet" key={row[0].id}>
          {row.map((item) => (
            <li key={item.id}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={`/api/v1/labels/qr?value=${encodeURIComponent(item.id)}`} alt={`QR ${item.payload}`} />
              <strong>{item.id}</strong>
            </li>
          ))}
        </ol>)}
      </div>
      {!printMode && totalPages > 1 && <nav aria-label="Halaman label">
        {currentPage > 1 && <Link href={pageLink(currentPage - 1)}>Sebelumnya</Link>}
        <span>Halaman {currentPage} dari {totalPages}</span>
        {currentPage < totalPages && <Link href={pageLink(currentPage + 1)}>Berikutnya</Link>}
      </nav>}
    </main>
  );
}
