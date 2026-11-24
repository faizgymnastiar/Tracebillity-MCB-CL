import 'server-only';
import { cookies } from 'next/headers';
import { matchingRole } from '@/lib/roles';
import type { AuthProfile, AuthProfileDto, OperatorContext, ProductionContext } from '@/types';

export const OPERATOR_COOKIE = 'trace_operator';
export const ADMIN_COOKIE = 'trace_admin';
/** Token sesi login dari trace-api; hanya server Next.js yang boleh membacanya. */
export const SESSION_COOKIE = 'trace_session';

export const PERMISSIONS = {
  view: 'traceability.view',
  write: 'traceability.write',
  label: 'traceability.label',
  history: 'traceability.history',
  packing: 'traceability.packing',
  admin: 'gov.admin',
} as const;

/** Hak operasi backend yang dianggap "boleh menulis" di layar traceability. */
const WRITE_HAK = ['scan.unit', 'scan.inner', 'scan.outer', 'ng.record'];
/** Hak generate/cetak label internal (Fase 2) — terpisah dari hak scan. */
const LABEL_HAK = ['print.manage'];
const HISTORY_HAK = ['history.read'];
const ADMIN_HAK = ['admin.master', 'admin.accounts'];

// Origin dikonfigurasi lokal agar session.ts tidak bergantung pada trace-backend.ts
// (menghindari circular import; trace-backend justru memakai helper di file ini).
const API_ORIGIN = (process.env.TRACE_API_ORIGIN ?? 'http://127.0.0.1:5080').replace(/\/+$/, '');
const API_INTERNAL_KEY = process.env.TRACE_API_INTERNAL_KEY ?? '';

async function readCookie(name: string): Promise<string | undefined> {
  try {
    const jar = await cookies();
    return jar.get(name)?.value;
  } catch {
    // Di luar request scope (mis. prerender build) cookies() melempar error.
    return undefined;
  }
}

export async function readSessionToken(): Promise<string | null> {
  const value = await readCookie(SESSION_COOKIE);
  return value && value.trim().length > 0 ? value : null;
}

export async function writeSessionToken(token: string): Promise<void> {
  const jar = await cookies();
  jar.set(SESSION_COOKIE, token, {
    httpOnly: true,
    sameSite: 'lax',
    path: '/',
    maxAge: 60 * 60 * 12,
  });
}

export async function clearSessionToken(): Promise<void> {
  const jar = await cookies();
  jar.delete(SESSION_COOKIE);
}

// ---------------------------------------------------------------------------
// Operator context cache — hanya dipakai untuk mode demo (enforcement mati).
// ---------------------------------------------------------------------------

function encodeContext(context: OperatorContext): string {
  return Buffer.from(JSON.stringify(context), 'utf8').toString('base64url');
}

function decodeContext(value: string | undefined): OperatorContext | null {
  if (!value) return null;
  try {
    const parsed: unknown = JSON.parse(Buffer.from(value, 'base64url').toString('utf8'));
    if (!parsed || typeof parsed !== 'object') return null;
    const candidate = parsed as Partial<OperatorContext>;
    if (!candidate.operator || !candidate.sku) return null;
    return {
      operator: candidate.operator,
      workOrder: candidate.workOrder ?? '-',
      materialLot: candidate.materialLot ?? '-',
      line: candidate.line ?? '-',
      shift: candidate.shift ?? '-',
      sku: candidate.sku,
      destination: candidate.destination ?? '-',
    };
  } catch {
    return null;
  }
}

export async function readOperatorContext(): Promise<OperatorContext | null> {
  return decodeContext(await readCookie(OPERATOR_COOKIE));
}

export async function requireOperatorContext(): Promise<OperatorContext | null> {
  return readOperatorContext();
}

export async function writeOperatorSession(context: OperatorContext): Promise<void> {
  const jar = await cookies();
  jar.set(OPERATOR_COOKIE, encodeContext(context), {
    httpOnly: true,
    sameSite: 'lax',
    path: '/',
    maxAge: 60 * 60 * 12,
  });
}

export async function clearOperatorSession(): Promise<void> {
  const jar = await cookies();
  jar.delete(OPERATOR_COOKIE);
}

// ---------------------------------------------------------------------------
// Admin PIN gate (warisan Super Admin Studio).
// ---------------------------------------------------------------------------

export async function isAdminSession(): Promise<boolean> {
  return (await readCookie(ADMIN_COOKIE)) === 'granted';
}

export async function writeAdminSession(): Promise<void> {
  const jar = await cookies();
  jar.set(ADMIN_COOKIE, 'granted', {
    httpOnly: true,
    sameSite: 'lax',
    path: '/',
    maxAge: 60 * 60 * 4,
  });
}

export async function clearAdminSession(): Promise<void> {
  const jar = await cookies();
  jar.delete(ADMIN_COOKIE);
}

// ---------------------------------------------------------------------------
// Profil dari backend — sumber kebenaran identitas & hak.
// ---------------------------------------------------------------------------

export interface TraceSessionState {
  token: string | null;
  profile: AuthProfile;
  production: ProductionContext | null;
  /** Null saat trace-api tidak bisa dihubungi (bukan sekadar belum login). */
  remote: AuthProfileDto | null;
}

const GUEST_PROFILE: AuthProfile = {
  username: 'guest',
  fullName: 'Guest Operator',
  department: 'Manufacturing Engineering',
  roles: ['traceability.viewer'],
  permissions: [PERMISSIONS.view],
  authenticated: false,
};

async function fetchAuthMe(token: string): Promise<{ offline: boolean; dto: AuthProfileDto | null }> {
  const headers: Record<string, string> = { accept: 'application/json' };
  headers['x-trace-session'] = token;
  if (API_INTERNAL_KEY) headers['x-trace-internal-key'] = API_INTERNAL_KEY;
  try {
    const response = await fetch(`${API_ORIGIN}/api/v1/auth/me`, { headers, cache: 'no-store' });
    if (response.status === 401 || response.status === 403) return { offline: false, dto: null };
    if (!response.ok) return { offline: false, dto: null };
    return { offline: false, dto: (await response.json()) as AuthProfileDto };
  } catch {
    return { offline: true, dto: null };
  }
}

function mapProfile(dto: AuthProfileDto): AuthProfile {
  const permissions: string[] = [PERMISSIONS.view, ...dto.permissions];
  if (dto.permissions.some((item) => WRITE_HAK.includes(item))) permissions.push(PERMISSIONS.write);
  if (dto.permissions.includes('scan.inner') || dto.permissions.includes('scan.outer')) permissions.push(PERMISSIONS.packing);
  if (dto.permissions.some((item) => LABEL_HAK.includes(item))) permissions.push(PERMISSIONS.label);
  if (dto.permissions.some((item) => HISTORY_HAK.includes(item))) permissions.push(PERMISSIONS.history);
  if (dto.permissions.some((item) => ADMIN_HAK.includes(item))) permissions.push(PERMISSIONS.admin);

  return {
    username: dto.email,
    fullName: dto.fullName || dto.email,
    department: 'Manufacturing Engineering',
    roles: [matchingRole(dto.permissions)?.label ?? (dto.permissions.length ? 'Akses khusus' : 'Tanpa izin modul')],
    permissions,
    authenticated: true,
  };
}

/** Satu kali jalan ke /auth/me; fallback profil lama dari cookie hanya saat API offline. */
export async function getTraceSession(): Promise<TraceSessionState> {
  const token = await readSessionToken();
  const legacyContext = await readOperatorContext();
  const admin = await isAdminSession();

  const legacyProfile = (): AuthProfile => {
    const permissions: string[] = [PERMISSIONS.view];
    if (legacyContext) {
      permissions.push(PERMISSIONS.write, PERMISSIONS.label, PERMISSIONS.history);
    }
    if (admin) permissions.push(PERMISSIONS.admin);
    return {
      username: legacyContext?.operator ?? 'guest',
      fullName: legacyContext?.operator ?? 'Guest Operator',
      department: 'Manufacturing Engineering',
      roles: admin
        ? ['traceability.operator', 'governance.admin']
        : legacyContext
          ? ['traceability.operator']
          : ['traceability.viewer'],
      permissions,
      authenticated: Boolean(legacyContext),
    };
  };

  if (!token) {
    // Tanpa token: mode demo memakai cache cookie; enforcement akan menolak sendiri.
    return { token, profile: legacyProfile(), production: null, remote: null };
  }

  const result = await fetchAuthMe(token);
  if (result.offline) {
    return { token, profile: legacyProfile(), production: null, remote: null };
  }
  if (!result.dto) {
    return { token, profile: GUEST_PROFILE, production: null, remote: null };
  }

  return { token, profile: mapProfile(result.dto), production: result.dto.production, remote: result.dto };
}

export async function getAuthProfile(): Promise<AuthProfile> {
  const session = await getTraceSession();
  return session.profile;
}
