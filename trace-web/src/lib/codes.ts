import { getWibParts } from './datetime';

export interface CodeContext {
  seqPadding: number;
  line: string;
  destination: string;
}

/**
 * Kode lot material dipakai apa adanya di barcode 2D, jadi keterangan dalam
 * tanda kurung dibuang agar payload tetap ringkas dan stabil saat di-scan.
 */
export function normalizeMaterialLotCode(rawLot: string | null | undefined): string {
  const clean = String(rawLot ?? '')
    .trim()
    .toUpperCase();
  if (!clean) return 'LOT-000';
  const codeOnly = clean.replace(/\s*\([^)]*\)\s*/g, '').trim();
  return codeOnly || clean;
}

export function destinationShortCode(destination: string | null | undefined): string {
  return String(destination ?? 'DEST')
    .split(' - ')[0]
    .replace(/[^A-Z0-9-]/gi, '')
    .toUpperCase();
}

export function lineShortCode(line: string | null | undefined): string {
  return String(line ?? 'L01')
    .replace(/[^A-Z0-9]/gi, '')
    .toUpperCase();
}

export function formatCustomCode(
  template: string,
  sequence: number,
  context: CodeContext,
  date: Date = new Date(),
): string {
  const parts = getWibParts(date);
  const pad = Math.max(2, Number(context.seqPadding) || 3);
  return (template || 'CODE-{SEQ}')
    .replace(/\{YYYY\}/g, parts.year)
    .replace(/\{YY\}/g, parts.year.slice(-2))
    .replace(/\{MM\}/g, parts.month)
    .replace(/\{DD\}/g, parts.day)
    .replace(/\{DEST\}/g, destinationShortCode(context.destination))
    .replace(/\{LINE\}/g, lineShortCode(context.line))
    .replace(/\{SEQ\}/g, String(sequence).padStart(pad, '0'));
}

export function displayValue(value: string | number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  const text = String(value).trim();
  return text === '' ? '—' : text;
}
