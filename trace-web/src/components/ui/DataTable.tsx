'use client';

import { useEffect, useMemo, useState } from 'react';
import type { MouseEvent as ReactMouseEvent, ReactNode } from 'react';
import type { DataTableColumn } from '@/types';
import { cn } from '@/lib/cn';
import { copyText, toCsv, toTsv } from '@/lib/download';
import { Icon } from './Icon';

interface GridLayoutCache {
  hidden?: string[];
  wrap?: boolean;
  pageSize?: number;
  widths?: Record<string, number>;
}

interface DataTableProps<T> {
  columns: DataTableColumn<T>[];
  rows: T[];
  rowKey: (row: T, index: number) => string;
  storageKey: string;
  title?: ReactNode;
  helper?: string;
  pageSizeOptions?: number[];
  initialPageSize?: number;
  toolbar?: ReactNode;
  onRowClick?: (row: T) => void;
}

function rawValue<T>(row: T, key: string): string {
  const value = (row as Record<string, unknown>)[key];
  if (value === null || value === undefined) return '';
  return String(value);
}

export function DataTable<T>({
  columns,
  rows,
  rowKey,
  storageKey,
  title,
  helper,
  pageSizeOptions = [25, 50, 100, 250],
  initialPageSize = 50,
  toolbar,
  onRowClick,
}: DataTableProps<T>) {
  const cacheKey = `lelco.grid.layout.v3.${storageKey}`;
  const [hidden, setHidden] = useState<string[]>([]);
  const [widths, setWidths] = useState<Record<string, number>>({});
  const [wrap, setWrap] = useState(true);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [page, setPage] = useState(1);
  const [sort, setSort] = useState<{ key: string; asc: boolean } | null>(null);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [menu, setMenu] = useState<{ key: string; x: number; y: number } | null>(null);
  const [status, setStatus] = useState('');
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(cacheKey);
      if (raw) {
        const parsed = JSON.parse(raw) as GridLayoutCache;
        if (Array.isArray(parsed.hidden)) setHidden(parsed.hidden);
        if (parsed.widths) setWidths(parsed.widths);
        if (typeof parsed.wrap === 'boolean') setWrap(parsed.wrap);
        if (typeof parsed.pageSize === 'number') setPageSize(parsed.pageSize);
      }
    } catch {
      // cache korup diabaikan supaya tabel tetap tampil dengan layout default
    }
    setHydrated(true);
  }, [cacheKey]);

  useEffect(() => {
    if (!hydrated) return;
    try {
      const payload: GridLayoutCache = { hidden, widths, wrap, pageSize };
      window.localStorage.setItem(cacheKey, JSON.stringify(payload));
    } catch {
      // penyimpanan penuh / private mode tidak menghalangi pemakaian tabel
    }
  }, [cacheKey, hidden, widths, wrap, pageSize, hydrated]);

  useEffect(() => {
    setPage(1);
  }, [rows, pageSize]);

  useEffect(() => {
    if (!status) return;
    const timer = window.setTimeout(() => setStatus(''), 2200);
    return () => window.clearTimeout(timer);
  }, [status]);

  const visibleColumns = useMemo(
    () => columns.filter((column) => !hidden.includes(column.key)),
    [columns, hidden],
  );

  const sortedRows = useMemo(() => {
    if (!sort) return rows;
    const column = columns.find((item) => item.key === sort.key);
    const next = [...rows];
    next.sort((left, right) => {
      const leftValue = column?.sortable === false ? '' : rawValue(left, sort.key);
      const rightValue = column?.sortable === false ? '' : rawValue(right, sort.key);
      const leftNumber = Number(leftValue.replaceAll('.', '').replace(',', '.'));
      const rightNumber = Number(rightValue.replaceAll('.', '').replace(',', '.'));
      const bothNumeric = leftValue !== '' && rightValue !== '' && !Number.isNaN(leftNumber) && !Number.isNaN(rightNumber);
      const comparison = bothNumeric
        ? leftNumber - rightNumber
        : leftValue.localeCompare(rightValue, 'id-ID');
      return sort.asc ? comparison : -comparison;
    });
    return next;
  }, [rows, sort, columns]);

  const totalPages = Math.max(1, Math.ceil(sortedRows.length / pageSize));
  const safePage = Math.min(page, totalPages);
  const pagedRows = sortedRows.slice((safePage - 1) * pageSize, safePage * pageSize);

  const toggleSort = (key: string) => {
    setSort((current) => {
      if (!current || current.key !== key) return { key, asc: true };
      if (current.asc) return { key, asc: false };
      return null;
    });
  };

  const hideColumn = (key: string) => {
    if (visibleColumns.length <= 1) return;
    setHidden((current) => (current.includes(key) ? current : [...current, key]));
    setMenu(null);
  };

  const startResize = (event: ReactMouseEvent<HTMLSpanElement>, key: string) => {
    event.preventDefault();
    event.stopPropagation();
    const startX = event.clientX;
    const startWidth = widths[key] ?? columns.find((column) => column.key === key)?.defaultWidth ?? 160;
    const onMove = (moveEvent: MouseEvent) => {
      const nextWidth = Math.max(80, startWidth + moveEvent.clientX - startX);
      setWidths((current) => ({ ...current, [key]: nextWidth }));
    };
    const onUp = () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  };

  const exportMatrix = () => {
    const used = visibleColumns;
    const headers = used.map((column) => column.label);
    const body = sortedRows.map((row) =>
      used.map((column) => column.exportValue?.(row) ?? rawValue(row, column.key)),
    );
    return { headers, body };
  };

  const handleCopy = async (format: 'csv' | 'tsv') => {
    const { headers, body } = exportMatrix();
    const text = format === 'csv' ? toCsv(headers, body) : toTsv(headers, body);
    const ok = await copyText(text);
    setStatus(ok ? `${format.toUpperCase()} ${body.length} baris disalin` : 'Gagal menyalin ke clipboard');
  };

  return (
    <div>
      <div className="erp-grid-toolbar">
        {title ? <h3 className="table-copy-title">{title}</h3> : <span />}
        <div className="erp-grid-export">
          <button type="button" onClick={() => handleCopy('tsv')}>Copy TSV</button>
          <button type="button" onClick={() => handleCopy('csv')}>Copy CSV</button>
          <button type="button" onClick={() => setWrap((current) => !current)}>{wrap ? 'No Wrap' : 'Wrap'}</button>
          <button type="button" onClick={() => setSettingsOpen(true)}>
            <Icon name="admin" size={13} /> Kolom
          </button>
          {toolbar}
        </div>
      </div>
      <div className="erp-grid-helper">
        {status || helper || 'Header sticky • klik judul kolom untuk urutkan • klik kanan untuk sembunyikan kolom'}
      </div>

      <div className="table-wrap">
        <table className={cn('erp-adjustable-grid', !wrap && 'grid-nowrap')}>
          <thead>
            <tr>
              {visibleColumns.map((column) => (
                <th
                  key={column.key}
                  style={{ width: widths[column.key] ?? column.defaultWidth ?? 160, position: 'relative' }}
                  onContextMenu={(event) => {
                    event.preventDefault();
                    setMenu({ key: column.key, x: event.clientX, y: event.clientY });
                  }}
                >
                  <button type="button" onClick={() => toggleSort(column.key)}>
                    {column.label}
                    {sort && sort.key === column.key ? (sort.asc ? ' ▲' : ' ▼') : ''}
                  </button>
                  <span
                    className="erp-column-resize"
                    role="separator"
                    aria-label={`Ubah lebar kolom ${column.label}`}
                    onMouseDown={(event) => startResize(event, column.key)}
                  />
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {pagedRows.length === 0 ? (
              <tr>
                <td className="empty" colSpan={visibleColumns.length}>
                  Belum ada data yang cocok dengan filter saat ini.
                </td>
              </tr>
            ) : (
              pagedRows.map((row, index) => (
                <tr
                  key={rowKey(row, index)}
                  style={onRowClick ? { cursor: 'pointer' } : undefined}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                >
                  {visibleColumns.map((column) => (
                    <td
                      key={column.key}
                      style={{ textAlign: column.align ?? 'left' }}
                      className={column.origin === 'calculated' ? 'col-origin-calculated' : undefined}
                    >
                      {column.render ? column.render(row, index) : rawValue(row, column.key)}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      <div className="table-foot">
        <span>
          {sortedRows.length} baris • halaman {safePage} dari {totalPages}
        </span>
        <div className="table-foot-controls">
          <select
            aria-label="Jumlah baris per halaman"
            value={pageSize}
            onChange={(event) => setPageSize(Number(event.target.value))}
          >
            {pageSizeOptions.map((option) => (
              <option key={option} value={option}>{option} / halaman</option>
            ))}
          </select>
          <button type="button" disabled={safePage <= 1} onClick={() => setPage(safePage - 1)}>‹ Prev</button>
          <span className="page-indicator">{safePage} / {totalPages}</span>
          <button type="button" disabled={safePage >= totalPages} onClick={() => setPage(safePage + 1)}>Next ›</button>
        </div>
      </div>

      {settingsOpen ? (
        <div className="ltn-modal-backdrop" role="dialog" aria-modal="true" onClick={() => setSettingsOpen(false)}>
          <div className="ltn-modal-card" onClick={(event) => event.stopPropagation()}>
            <div className="dialog-head">
              <h2>Tampilkan Kolom</h2>
              <button type="button" onClick={() => setSettingsOpen(false)} aria-label="Tutup">
                <Icon name="close" />
              </button>
            </div>
            <div className="erp-grid-column-list">
              {columns.map((column) => (
                <label key={column.key}>
                  <input
                    type="checkbox"
                    checked={!hidden.includes(column.key)}
                    onChange={() => {
                      if (hidden.includes(column.key)) {
                        setHidden((current) => current.filter((key) => key !== column.key));
                      } else {
                        hideColumn(column.key);
                      }
                    }}
                  />
                  {column.label}
                </label>
              ))}
            </div>
          </div>
        </div>
      ) : null}

      {menu ? (
        <>
          <div style={{ position: 'fixed', inset: 0, zIndex: 999 }} onClick={() => setMenu(null)} />
          <div className="erp-grid-context" style={{ left: menu.x, top: menu.y }}>
            <button type="button" onClick={() => hideColumn(menu.key)}>Sembunyikan kolom ini</button>
            <button type="button" onClick={() => setMenu(null)}>Tutup menu</button>
          </div>
        </>
      ) : null}
    </div>
  );
}
