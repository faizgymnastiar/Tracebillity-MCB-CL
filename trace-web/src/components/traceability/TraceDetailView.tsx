import Link from 'next/link';
import type { TraceDetailResponse, TraceabilityRecord } from '@/types';
import { cn } from '@/lib/cn';
import { formatDateDisplay, formatNumber } from '@/lib/formatters';
import { Icon } from '@/components/ui/Icon';
import { EmptyState, PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';

interface TraceDetailViewProps {
  detail: TraceDetailResponse;
}

function toneOf(status: string): 'ok' | 'ng' | 'rework' | 'neutral' {
  const value = status.toUpperCase();
  if (value.includes('NG') || value.includes('SCRAP') || value.includes('FAIL')) return 'ng';
  if (value.includes('REWORK') || value.includes('RWK') || value.includes('TICKET')) return 'rework';
  if (value.includes('OK') || value.includes('FULL') || value.includes('PASS')) return 'ok';
  return 'neutral';
}

function statusTone(record: TraceabilityRecord): 'ok' | 'ng' | 'rework' {
  if (record.category === 'NG') return 'ng';
  if (record.category === 'REWORK') return 'rework';
  return 'ok';
}

export function TraceDetailView({ detail }: TraceDetailViewProps) {
  const title =
    detail.kind === 'unit'
      ? `Unit ${detail.query}`
      : detail.kind === 'inner'
        ? `Inner Box ${detail.query}`
        : `Outer Box ${detail.query}`;

  return (
    <>
      <PageHeader
        eyebrow={`TRACEABILITY • GENEALOGY ${detail.kind.toUpperCase()}`}
        title={title}
        description="Jejak lengkap dari gerbang akhir lini hingga kemasan dan tujuan pengiriman."
        actions={
          <Link href="/modules/traceability/database" className="hero-button ghost"
            style={{ borderColor: '#cbd5e1', color: '#161b41', padding: '8px 12px' }}>
            <Icon name="arrow-right" size={14} /> Kembali ke database
          </Link>
        }
      />

      <section className="split-grid">
        <Panel title="Timeline genealogy" actions={<StatusBadge tone="neutral">{detail.timeline.length} langkah</StatusBadge>}>
          {detail.timeline.length === 0 ? (
            <EmptyState>Belum ada langkah timeline untuk entitas ini.</EmptyState>
          ) : (
            <ol className="timeline">
              {detail.timeline.map((step, index) => (
                <li className="timeline-step" key={`${step.label}-${index}`}>
                  <span className={cn('timeline-dot', step.tone !== 'ok' && step.tone)} />
                  <div>
                    <b>{step.label}</b>
                    <p>{step.detail}</p>
                    <span className="timeline-time mono">{step.time ?? '—'}</span>
                  </div>
                </li>
              ))}
            </ol>
          )}
        </Panel>

        <div className="stack">
          {detail.unit ? (
            <Panel
              title="Detail unit"
              actions={<StatusBadge tone={statusTone(detail.unit)}>{detail.unit.status}</StatusBadge>}
            >
              <dl className="label-meta">
                <dt>Serial Number</dt>
                <dd className="mono">{detail.unit.sn}</dd>
                <dt>Tanggal</dt>
                <dd className="mono">{formatDateDisplay(detail.unit.isoDate)} • {detail.unit.waktu}</dd>
                <dt>Model / SKU</dt>
                <dd>{detail.unit.sku}</dd>
                <dt>Work Order</dt>
                <dd className="mono">{detail.unit.wo}</dd>
                <dt>Material Lot</dt>
                <dd className="mono">{detail.unit.lot}</dd>
                <dt>Inner Box</dt>
                <dd className="mono">{detail.unit.innerBox}</dd>
                <dt>Outer Box</dt>
                <dd className="mono">{detail.unit.outerBox}</dd>
                <dt>Tiket Rework</dt>
                <dd className="mono">{detail.unit.reworkTicket}</dd>
                <dt>Line / Shift</dt>
                <dd>{detail.unit.line} • {detail.unit.shift}</dd>
                <dt>Operator</dt>
                <dd>{detail.unit.operator}</dd>
                <dt>Tujuan</dt>
                <dd>{detail.unit.destination}</dd>
                <dt>Keterangan</dt>
                <dd>{detail.unit.keterangan}</dd>
              </dl>
            </Panel>
          ) : null}

          {detail.innerBox ? (
            <Panel
              title="Isi inner box"
              actions={
                <StatusBadge tone="ok">
                  {detail.innerBox.items.length} / {formatNumber(detail.innerBox.items.length)} Pcs
                  {detail.innerBox.partial ? ' • PARTIAL' : ''}
                </StatusBadge>
              }
            >
              <dl className="label-meta" style={{ marginBottom: 12 }}>
                <dt>Inner Box</dt>
                <dd className="mono">{detail.innerBox.id}</dd>
                <dt>SKU</dt>
                <dd>{detail.innerBox.sku}</dd>
                <dt>Work Order</dt>
                <dd className="mono">{detail.innerBox.workOrder}</dd>
                <dt>Material Lot</dt>
                <dd className="mono">{detail.innerBox.materialLot}</dd>
                <dt>Diserahkan</dt>
                <dd className="mono">{detail.innerBox.time}</dd>
                <dt>Tujuan</dt>
                <dd>{detail.innerBox.destination}</dd>
              </dl>
              <div className="list">
                {detail.innerBox.items.map((item, index) => (
                  <div className="list-row" key={item.sn}>
                    <div>
                      <b className="mono">
                        {index + 1}. {item.sn}
                      </b>
                      <p>
                        {item.operator} • {item.shift} • {item.time}
                      </p>
                    </div>
                    <div className="list-tail">
                      {item.reworked ? <span className="badge rw">RWK #{item.reworkCycle}</span> : <span className="badge ok">OK</span>}
                    </div>
                  </div>
                ))}
              </div>
            </Panel>
          ) : null}

          {detail.outerBox ? (
            <Panel
              title="Isi outer box"
              actions={
                <StatusBadge tone="ok">
                  {detail.outerBox.inners.length} Inner • {formatNumber(detail.outerBox.totalPcs)} Pcs
                  {detail.outerBox.partial ? ' • PARTIAL' : ''}
                </StatusBadge>
              }
            >
              <dl className="label-meta" style={{ marginBottom: 12 }}>
                <dt>Outer Box</dt>
                <dd className="mono">{detail.outerBox.id}</dd>
                <dt>SKU</dt>
                <dd>{detail.outerBox.sku}</dd>
                <dt>Work Order</dt>
                <dd className="mono">{detail.outerBox.workOrder}</dd>
                <dt>Material Lot</dt>
                <dd className="mono">{detail.outerBox.materialLot}</dd>
                <dt>Diserahkan</dt>
                <dd className="mono">{detail.outerBox.time}</dd>
                <dt>Tujuan</dt>
                <dd>{detail.outerBox.destination}</dd>
              </dl>
              <div className="chip-row">
                {detail.outerBox.inners.map((innerId) => (
                  <Link key={innerId} href={`/modules/traceability/database/${encodeURIComponent(innerId)}`} className="chip ok">
                    <b className="mono">{innerId}</b>
                  </Link>
                ))}
              </div>
            </Panel>
          ) : null}
        </div>
      </section>

      <section className="split-grid">
        <Panel title="Unit terkait" actions={<span className="helper">{detail.relatedUnits.length} unit</span>}>
          {detail.relatedUnits.length === 0 ? (
            <EmptyState>Tidak ada unit lain pada kemasan ini.</EmptyState>
          ) : (
            <div className="chip-row">
              {detail.relatedUnits.map((unit) => (
                <Link key={unit.sn} href={unit.href} className={cn('chip', toneOf(unit.status) === 'ng' ? 'ng' : 'ok')}>
                  <b className="mono">{unit.sn}</b>
                  <span>{unit.status}</span>
                </Link>
              ))}
            </div>
          )}
        </Panel>

        <Panel title="Kemasan terkait" actions={<span className="helper">{detail.relatedBoxes.length} box</span>}>
          {detail.relatedBoxes.length === 0 ? (
            <EmptyState>Unit ini belum terhubung ke kemasan lain.</EmptyState>
          ) : (
            <div className="chip-row">
              {detail.relatedBoxes.map((box) => (
                <Link key={`${box.type}-${box.id}`} href={box.href} className={cn('chip', box.type === 'OUTER' ? 'rw' : 'ok')}>
                  {box.type}
                  <b className="mono">{box.label}</b>
                </Link>
              ))}
            </div>
          )}
        </Panel>
      </section>
    </>
  );
}
