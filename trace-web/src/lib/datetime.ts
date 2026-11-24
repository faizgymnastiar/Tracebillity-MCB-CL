const TIME_ZONE = 'Asia/Jakarta';

export const WIB_LABEL = 'WIB';

export interface WibParts {
  year: string;
  month: string;
  day: string;
  hour: string;
  minute: string;
  second: string;
}

const PART_FORMATTER = new Intl.DateTimeFormat('en-GB', {
  timeZone: TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});

const WIB_DATE_TIME_FORMATTER = new Intl.DateTimeFormat('id-ID', {
  timeZone: TIME_ZONE,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

const DAY_FORMATTER = new Intl.DateTimeFormat('id-ID', {
  timeZone: TIME_ZONE,
  weekday: 'long',
});

const LONG_DATE_FORMATTER = new Intl.DateTimeFormat('id-ID', {
  timeZone: TIME_ZONE,
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});

export function getWibParts(date: Date = new Date()): WibParts {
  const parts = PART_FORMATTER.formatToParts(date);
  const lookup = (type: Intl.DateTimeFormatPartTypes): string =>
    parts.find((part) => part.type === type)?.value ?? '00';
  return {
    year: lookup('year'),
    month: lookup('month'),
    day: lookup('day'),
    hour: lookup('hour'),
    minute: lookup('minute'),
    second: lookup('second'),
  };
}

/** Stamp ISO (UTC) dari backend → "dd-MM-yyyy HH:mm WIB"; nilai kosong tampil tanda strip. */
export function formatWibDateTime(value: string | Date | null | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return `${WIB_DATE_TIME_FORMATTER.format(date)} WIB`;
}

export function getIsoDate(date: Date = new Date()): string {
  const { year, month, day } = getWibParts(date);
  return `${year}-${month}-${day}`;
}

export function getClockWib(date: Date = new Date()): string {
  const { hour, minute, second } = getWibParts(date);
  return `${hour}:${minute}:${second}`;
}

export function getDayName(date: Date = new Date()): string {
  return DAY_FORMATTER.format(date).toUpperCase();
}

export function getLongDateWib(date: Date = new Date()): string {
  return LONG_DATE_FORMATTER.format(date);
}

export function getFullTimestamp(date: Date = new Date()): string {
  const { year, month, day } = getWibParts(date);
  return `${getDayName(date)}, ${day}/${month}/${year} ${getClockWib(date)} WIB`;
}

export function parseIsoDate(isoDate: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoDate);
  if (!match) return null;
  return new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), 12, 0, 0));
}

export function isoDateToDayName(isoDate: string): string {
  const parsed = parseIsoDate(isoDate);
  return parsed ? getDayName(parsed) : '-';
}

export function shiftIso(isoDate: string, days: number): string {
  const parsed = parseIsoDate(isoDate);
  if (!parsed) return isoDate;
  parsed.setUTCDate(parsed.getUTCDate() + days);
  return parsed.toISOString().slice(0, 10);
}

export function makeWibTimestamp(isoDate: string, time: string): string {
  const [year, month, day] = isoDate.split('-');
  return `${isoDateToDayName(isoDate)}, ${day}/${month}/${year} ${time} WIB`;
}

export function getDateStamp(date: Date = new Date()): string {
  const { year, month, day } = getWibParts(date);
  return `${year}${month}${day}`;
}

export function getMonthStamp(date: Date = new Date()): string {
  const { year, month } = getWibParts(date);
  return `${year}${month}`;
}
