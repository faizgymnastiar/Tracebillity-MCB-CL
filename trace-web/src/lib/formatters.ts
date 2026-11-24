const EMPTY = '—';

export function formatNumber(value: number | null | undefined, fractionDigits = 0): string {
  if (value === null || value === undefined || Number.isNaN(value)) return EMPTY;
  return new Intl.NumberFormat('id-ID', {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  }).format(value);
}

export function formatIdr(value: number | null | undefined, fractionDigits = 2): string {
  if (value === null || value === undefined || Number.isNaN(value)) return EMPTY;
  return `Rp ${formatNumber(value, fractionDigits)}`;
}

export function formatDateDisplay(value: string | Date | null | undefined): string {
  if (!value) return EMPTY;
  if (value instanceof Date) {
    if (Number.isNaN(value.getTime())) return EMPTY;
    const day = String(value.getDate()).padStart(2, '0');
    const month = String(value.getMonth() + 1).padStart(2, '0');
    return `${day}-${month}-${value.getFullYear()}`;
  }
  const matched = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!matched) return value;
  return `${matched[3]}-${matched[2]}-${matched[1]}`;
}

export function formatPercent(numerator: number, denominator: number): string {
  if (!denominator) return '0%';
  return `${Math.round((numerator / denominator) * 100)}%`;
}

export function padSeries(value: number, width = 3): string {
  return String(Math.max(0, Math.trunc(value))).padStart(width, '0');
}

export function formatQty(value: number): string {
  return `${formatNumber(value)} Pcs`;
}

export function emptyDisplay(value: string | number | null | undefined): string {
  if (value === null || value === undefined) return EMPTY;
  const text = String(value).trim();
  return text === '' ? EMPTY : text;
}
