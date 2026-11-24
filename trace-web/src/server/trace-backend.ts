import 'server-only';
import type {
  AppConfig,
  BridgePayload,
  DailySummaryResponse,
  HealthStatus,
  HomeStats,
  LabelStockSummary,
  LoginResultDto,
  MasterListKey,
  NgStationInfo,
  NgWorkspaceSummary,
  OperatorContext,
  PublicConfig,
  ScanSnapshot,
  TraceDetailResponse,
  TraceRecordListResponse,
} from '@/types';
import { DEFAULT_CONFIG } from '@/lib/config';
import { readSessionToken } from './session';

/**
 * Klien server-side untuk trace-api (.NET). Browser tidak pernah memanggil origin ini:
 * seluruh trafik lewat route handler / server component Next.js.
 */
export const TRACE_API_ORIGIN = (process.env.TRACE_API_ORIGIN ?? 'http://127.0.0.1:5080').replace(/\/+$/, '');

const INTERNAL_KEY = process.env.TRACE_API_INTERNAL_KEY ?? '';

export interface MasterListsResponse {
  keys: MasterListKey[];
  labels: Record<MasterListKey, string>;
  lists: Record<MasterListKey, string[]>;
}

export interface RecordsQuery {
  search?: string;
  date?: string;
  status?: string;
  page?: number;
  pageSize?: number;
  columns?: string;
}

export interface ScanPayload {
  mode: 'OK' | 'NG' | 'REWORK';
  sn: string;
  stationId?: string;
  reason?: string;
  disposition?: string;
  action?: string;
  verdict?: 'PASS' | 'FAIL';
}

export interface TraceApiRequest {
  method?: string;
  body?: string;
  context?: OperatorContext | null;
  admin?: boolean;
  /** Token sesi login (X-Trace-Session). Bila undefined, dibaca otomatis dari cookie. */
  sessionToken?: string | null;
  /** Versi konteks pada layar (X-Trace-Context-Version) untuk penolakan konteks lama. */
  contextVersion?: number | null;
  query?: Record<string, string | number | undefined>;
}

/** Dilempar saat trace-api (.NET) tidak dapat dihubungi sama sekali (ECONNREFUSED, DNS, timeout). */
export class TraceApiOfflineError extends Error {
  constructor(origin: string) {
    super(`Trace API tidak dapat dihubungi di ${origin}.`);
    this.name = 'TraceApiOfflineError';
  }
}

export function isTraceApiOffline(error: unknown): boolean {
  return error instanceof TraceApiOfflineError;
}

function fallbackPublicConfig(): PublicConfig {
  const clone: Partial<AppConfig> = { ...DEFAULT_CONFIG };
  delete clone.adminPin;
  return clone as PublicConfig;
}

function offlineHealth(): HealthStatus {
  return { status: 'offline', dataSource: 'trace-api', latestSyncIso: null, rowsProcessed: null };
}

export function buildTraceUrl(path: string, query?: TraceApiRequest['query']): string {
  const normalized = path.startsWith('/') ? path : `/${path}`;
  const url = new URL(`${TRACE_API_ORIGIN}${normalized}`);
  Object.entries(query ?? {}).forEach(([key, value]) => {
    if (value === undefined || value === '') return;
    url.searchParams.set(key, String(value));
  });
  return url.toString();
}

export function buildTraceHeaders(request: TraceApiRequest = {}): HeadersInit {
  const headers: Record<string, string> = { accept: 'application/json' };
  if (request.body !== undefined) headers['content-type'] = 'application/json';
  if (INTERNAL_KEY) headers['x-trace-internal-key'] = INTERNAL_KEY;
  if (request.sessionToken) headers['x-trace-session'] = request.sessionToken;
  if (typeof request.contextVersion === 'number') headers['x-trace-context-version'] = String(request.contextVersion);
  if (request.admin) headers['x-trace-admin'] = 'granted';
  if (request.context) {
    headers['x-trace-operator'] = request.context.operator;
    headers['x-trace-work-order'] = request.context.workOrder;
    headers['x-trace-material-lot'] = request.context.materialLot;
    headers['x-trace-line'] = request.context.line;
    headers['x-trace-shift'] = request.context.shift;
    headers['x-trace-sku'] = request.context.sku;
    headers['x-trace-destination'] = request.context.destination;
  }
  return headers;
}

export async function callTraceApi(path: string, request: TraceApiRequest = {}): Promise<Response> {
  const enriched: TraceApiRequest =
    request.sessionToken === undefined ? { ...request, sessionToken: await readSessionToken() } : request;
  try {
    return await fetch(buildTraceUrl(path, enriched.query), {
      method: enriched.method ?? 'GET',
      headers: buildTraceHeaders(enriched),
      body: enriched.body,
      cache: 'no-store',
    });
  } catch (error) {
    if (error instanceof Error) throw new TraceApiOfflineError(TRACE_API_ORIGIN);
    throw error;
  }
}

export async function readTraceJson<T>(response: Response): Promise<T> {
  return (await response.json()) as T;
}

async function getJson<T>(path: string, request: TraceApiRequest = {}): Promise<T> {
  const response = await callTraceApi(path, request);
  if (!response.ok) throw new Error(`trace-api ${response.status} pada ${path}`);
  return readTraceJson<T>(response);
}

/** Probe murah untuk halaman: apakah backend .NET sedang berjalan. */
export async function isTraceApiReachable(): Promise<boolean> {
  try {
    const response = await callTraceApi('/api/v1/health');
    return response.ok;
  } catch (error) {
    if (isTraceApiOffline(error)) return false;
    throw error;
  }
}

export async function getHealth(): Promise<HealthStatus> {
  try {
    return await getJson<HealthStatus>('/api/v1/health');
  } catch (error) {
    if (isTraceApiOffline(error)) return offlineHealth();
    throw error;
  }
}

export async function getPublicConfig(): Promise<PublicConfig> {
  try {
    return await getJson<PublicConfig>('/api/v1/config/public');
  } catch (error) {
    if (isTraceApiOffline(error)) return fallbackPublicConfig();
    throw error;
  }
}

export function getMasterLists(): Promise<MasterListsResponse> {
  return getJson<MasterListsResponse>('/api/v1/master/lists');
}

export function getScanState(context?: OperatorContext | null): Promise<ScanSnapshot> {
  return getJson<ScanSnapshot>('/api/v1/traceability/scan-state', { context });
}

export function getDailySummary(context?: OperatorContext | null): Promise<DailySummaryResponse> {
  return getJson<DailySummaryResponse>('/api/v1/traceability/daily-summary', { context });
}

export function getHomeStats(context?: OperatorContext | null): Promise<HomeStats> {
  return getJson<HomeStats>('/api/v1/traceability/home-stats', { context });
}

export function getBridgePayload(context: OperatorContext): Promise<BridgePayload> {
  return getJson<BridgePayload>('/api/v1/traceability/bridge', { context });
}

export async function getTraceDetail(
  id: string,
  context?: OperatorContext | null,
): Promise<TraceDetailResponse | null> {
  const response = await callTraceApi(`/api/v1/traceability/records/${encodeURIComponent(id)}`, { context });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`trace-api ${response.status} untuk data trace ${id}`);
  return readTraceJson<TraceDetailResponse>(response);
}

export function listRecords(query: RecordsQuery, context?: OperatorContext | null): Promise<TraceRecordListResponse> {
  return getJson<TraceRecordListResponse>('/api/v1/traceability/records', {
    context,
    query: {
      search: query.search,
      date: query.date,
      status: query.status,
      page: query.page,
      pageSize: query.pageSize,
    },
  });
}

/** Ringkasan stok label internal NG + kondisi sequence penerbitan (Fase 2). */
export function getLabelSummary(context?: OperatorContext | null): Promise<LabelStockSummary> {
  return getJson<LabelStockSummary>('/api/v1/labels', { context });
}

/** Ringkasan karantina/rework/OK akhir untuk layar Fase 3. */
export function getNgSummary(context?: OperatorContext | null): Promise<NgWorkspaceSummary> {
  return getJson<NgWorkspaceSummary>('/api/v1/ng/summary', { context });
}

/** Pos QC + defect aktif keluarga SKU sesi pelaku (dropdown server-side). */
export function getNgStations(context?: OperatorContext | null): Promise<NgStationInfo[]> {
  return getJson<NgStationInfo[]>('/api/v1/ng/stations', { context });
}

export async function verifyAdminPin(pin: string, sessionToken?: string | null): Promise<boolean> {
  try {
    const response = await callTraceApi('/api/v1/auth/verify-pin', {
      method: 'POST',
      body: JSON.stringify({ pin }),
      sessionToken,
    });
    if (!response.ok) return false;
    const result = await readTraceJson<{ valid?: boolean }>(response);
    return result.valid === true;
  } catch (error) {
    if (isTraceApiOffline(error)) return false;
    throw error;
  }
}

/** Sinkronisasi konteks bersifat best-effort: sesi cookie tetap sah walau API sedang mati. */
export async function putSessionContext(context: OperatorContext): Promise<void> {
  try {
    await callTraceApi('/api/v1/session/context', { method: 'PUT', body: JSON.stringify(context), sessionToken: null });
  } catch (error) {
    if (!isTraceApiOffline(error)) throw error;
  }
}

// ---------------------------------------------------------------------------
// Klien fondasi Fase 1 — login email/password dipakai route handler auth.
// ---------------------------------------------------------------------------

export interface LoginFailure {
  ok: false;
  status: number;
  error: string;
  code?: string;
}

export type LoginOutcome = { ok: true; result: LoginResultDto } | LoginFailure;

async function readErrorPayload(response: Response, fallback: string): Promise<LoginFailure> {
  let message = fallback;
  let code: string | undefined;
  try {
    const payload = (await response.json()) as { error?: string; code?: string };
    message = payload.error ?? fallback;
    code = payload.code;
  } catch {
    /* body bukan JSON */
  }
  return { ok: false, status: response.status, error: message, code };
}

export async function loginWithPassword(email: string, password: string): Promise<LoginOutcome> {
  try {
    const response = await callTraceApi('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
      sessionToken: null,
    });
    if (!response.ok) return readErrorPayload(response, 'Login ditolak.');
    return { ok: true, result: (await response.json()) as LoginResultDto };
  } catch (error) {
    if (isTraceApiOffline(error)) {
      return { ok: false, status: 502, error: 'Trace API tidak dapat dihubungi.' };
    }
    throw error;
  }
}

export async function logoutSession(token: string | null): Promise<void> {
  if (!token) return;
  try {
    await callTraceApi('/api/v1/auth/logout', { method: 'POST', sessionToken: token });
  } catch (error) {
    if (!isTraceApiOffline(error)) throw error;
  }
}
