export function downloadTextFile(filename: string, content: string, mime = 'text/plain;charset=utf-8'): void {
  if (typeof window === 'undefined') return;
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

function escapeCsvCell(value: string | number | null | undefined, delimiter: string): string {
  if (value === null || value === undefined) return '';
  const text = String(value);
  const needsQuote = text.includes(delimiter) || text.includes('"') || text.includes('\n') || text.includes('\r');
  if (!needsQuote) return text;
  return `"${text.replaceAll('"', '""')}"`;
}

export function toDelimited(
  headers: string[],
  rows: Array<Array<string | number | null | undefined>>,
  delimiter = ',',
): string {
  const lines = [headers, ...rows].map((cells) => cells.map((cell) => escapeCsvCell(cell, delimiter)).join(delimiter));
  return lines.join('\r\n');
}

export function toCsv(headers: string[], rows: Array<Array<string | number | null | undefined>>): string {
  return toDelimited(headers, rows, ',');
}

export function toTsv(headers: string[], rows: Array<Array<string | number | null | undefined>>): string {
  return toDelimited(headers, rows, '\t');
}

export async function copyText(content: string): Promise<boolean> {
  if (typeof navigator === 'undefined' || !navigator.clipboard) return false;
  try {
    await navigator.clipboard.writeText(content);
    return true;
  } catch {
    return false;
  }
}
