import Link from 'next/link';
import { Panel } from '@/components/ui/SharedUi';
import { formatDateDisplay, formatNumber } from '@/lib/formatters';
import { callTraceApi } from '@/server/trace-backend';

interface Report {
  totals: { total: number; ok: number; ngUnits: number; quarantine: number; retest: number; hold: number; awaitingInner: number };
  daily: { day: string; total: number; ng: number }[];
  defects: { stage: string; group: string; name: string; count: number }[];
  products: string[];
}

type Search = Record<string, string | string[] | undefined>;

export async function AdminProductionDashboard({ query }: { query: Search }) {
  const today = new Date(Date.now() + 7 * 3600000);
  const earliest = new Date(today.getTime() - 13 * 86400000);
  const read = (key: string, fallback = '') => {
    const value = query[key];
    return typeof value === 'string' ? value : fallback;
  };
  const filters = {
    from: read('from', earliest.toISOString().slice(0, 10)),
    to: read('to', today.toISOString().slice(0, 10)),
    sku: read('sku'),
    wo: read('wo'),
  };

  let report: Report | null = null;
  let error = '';
  try {
    const response = await callTraceApi('/api/v1/packing/production-report', { query: filters });
    if (response.ok) report = await response.json() as Report;
    else if (response.status === 400) error = (await response.json() as { error?: string }).error ?? 'Filter tidak valid.';
    else error = 'Laporan produksi gagal dimuat.';
  } catch {
    error = 'Laporan produksi gagal dimuat.';
  }

  return <section className="admin-production" aria-label="Laporan produksi">
    <Panel title="Filter laporan">
      <form action="/" method="get" className="admin-report-filters">
        <label>Dari tanggal (WIB)<input type="date" name="from" defaultValue={filters.from} /></label>
        <label>Sampai tanggal (WIB)<input type="date" name="to" defaultValue={filters.to} /></label>
        <label>Produk / SKU<select name="sku" defaultValue={filters.sku}>
          <option value="">Semua SKU</option>
          {filters.sku && !report?.products.includes(filters.sku) && <option>{filters.sku}</option>}
          {report?.products.map((sku) => <option key={sku}>{sku}</option>)}
        </select></label>
        <label>Work order<input name="wo" placeholder="Nomor WO" defaultValue={filters.wo} /></label>
        <button className="btn-primary">Terapkan filter</button>
        <Link href="/">Reset</Link>
      </form>
      <form method="get" action="/modules/traceability/database" className="admin-report-filters admin-trace-search">
        <label>Telusuri serial<input name="search" placeholder="Ketik atau pindai serial" required /></label>
        <button type="submit">Telusuri unit</button>
      </form>
    </Panel>
    {error && <p role="alert" className="scan-alert error">{error}</p>}
    {report && <ReportCharts report={report} />}
  </section>;
}

function ReportCharts({ report }: { report: Report }) {
  const { totals, daily, defects } = report;
  const highestDay = Math.max(1, ...daily.map((item) => item.total));
  const tickSize = Math.ceil(highestDay / 4);
  const scale = tickSize * 4;
  const highestDefect = defects[0]?.count ?? 1;
  const ngRate = totals.total ? totals.ngUnits / totals.total : 0;
  const percent = new Intl.NumberFormat('id-ID', { style: 'percent', maximumFractionDigits: 1 });
  return <>
    <div className="admin-report-kpis">
      <Panel title="Unit tercatat"><strong>{formatNumber(totals.total)}</strong></Panel>
      <Panel title="OK akhir"><strong className="admin-report-ok">{formatNumber(totals.ok)}</strong></Panel>
      <Panel title="Pernah NG"><strong className="admin-report-ng">{formatNumber(totals.ngUnits)}</strong></Panel>
      <Panel title="Persentase unit NG"><strong>{percent.format(ngRate)}</strong></Panel>
    </div>
    <div className="admin-report-charts">
      <Panel title="Tren unit harian">
        <p className="helper">Per tanggal unit pertama kali tercatat di WIB. Status mencerminkan kondisi saat ini.</p>
        <div className="admin-report-legend"><span>Tanpa riwayat NG</span><span>Pernah NG</span></div>
        {daily.length ? <div className="admin-column-chart-scroll" tabIndex={0} role="region" aria-label="Grafik batang unit harian">
          <div className="admin-column-chart">
            <div className="admin-chart-axis" aria-hidden="true">
              {[4, 3, 2, 1, 0].map((tick) => <span key={tick}>{formatNumber(tick * tickSize)}</span>)}
            </div>
            <ol className="admin-chart-columns">{daily.map((item) => {
              const label = `${formatDateDisplay(item.day)} · ${formatNumber(item.total)} unit · ${formatNumber(item.ng)} pernah NG`;
              return <li key={item.day} title={label} aria-label={label}>
                <div className="admin-chart-stack" aria-hidden="true">
                  <span className="admin-report-bar" style={{ height: `${(item.total - item.ng) / scale * 100}%` }} />
                  <span className="admin-report-bar ng" style={{ height: `${item.ng / scale * 100}%` }} />
                </div>
                <time dateTime={item.day}>{item.day.slice(8, 10)}/{item.day.slice(5, 7)}</time>
              </li>;
            })}</ol>
          </div>
        </div> : <p className="helper">Belum ada unit pada filter ini.</p>}
      </Panel>
      <Panel title="Proporsi unit NG">
        {totals.total > 0 ? <>
          <div className="admin-chart-donut" style={{ background: `conic-gradient(#aa3b52 ${ngRate * 100}%, #187977 0)` }}>
            <div><strong>{percent.format(ngRate)}</strong><span>Pernah NG</span></div>
          </div>
          <div className="admin-report-legend admin-donut-legend">
            <span>Tanpa riwayat NG ({formatNumber(totals.total - totals.ngUnits)})</span>
            <span>Pernah NG ({formatNumber(totals.ngUnits)})</span>
          </div>
        </> : <p className="helper">Belum ada unit pada filter ini.</p>}
      </Panel>
    </div>
    <div className="admin-report-charts">
      <Panel title="Kategori NG terbanyak">
        <p className="helper">Hingga 8 kategori menurut jumlah kejadian dalam periode. Satu unit bisa mengalami beberapa NG.</p>
        {defects.length ? <ol className="admin-report-bars">{defects.map((item) => <li key={JSON.stringify([item.stage, item.group, item.name])}>
          <div><span>{item.group} · {item.name} <small>({item.stage})</small></span><strong>{formatNumber(item.count)}</strong></div>
          <div className="admin-report-track" aria-hidden="true">
            <span className="admin-report-bar ng" style={{ width: `${item.count / highestDefect * 100}%` }} />
          </div>
        </li>)}</ol> : <p className="helper">Belum ada kejadian NG pada filter ini.</p>}
      </Panel>
    <Panel title="Perlu perhatian">
      <dl className="admin-report-attention">
        <div><dt>Tahan QC</dt><dd className="admin-report-ng">{formatNumber(totals.hold)}</dd></div>
        <div><dt>Dalam karantina</dt><dd>{formatNumber(totals.quarantine)}</dd></div>
        <div><dt>Menunggu uji ulang</dt><dd>{formatNumber(totals.retest)}</dd></div>
        <div><dt>OK akhir belum masuk inner</dt><dd>{formatNumber(totals.awaitingInner)}</dd></div>
      </dl>
    </Panel>
    </div>
  </>;
}
