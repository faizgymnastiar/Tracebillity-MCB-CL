import { NextResponse } from 'next/server';
import { callTraceApi } from './trace-backend';
import { isAdminSession, readOperatorContext, readSessionToken } from './session';

export const OPERATOR_REQUIRED_MESSAGE =
  'Sesi login tidak ditemukan atau sudah berakhir. Silakan login ulang.';

export interface ProxyOptions {
  method?: string;
  body?: string;
  admin?: boolean;
  requireOperator?: boolean;
  /** Versi konteks dari layar pengirim; backend menolak transaksi basi bila beda. */
  contextVersion?: number | null;
  query?: Record<string, string | number | undefined>;
}

function upstreamError(): NextResponse {
  return NextResponse.json({ error: 'Trace API tidak dapat dihubungi.' }, { status: 502 });
}

function passthrough(response: Response, payload: string): NextResponse {
  return new NextResponse(payload, {
    status: response.status,
    headers: {
      'content-type': response.headers.get('content-type') ?? 'application/json; charset=utf-8',
      'cache-control': 'no-store',
    },
  });
}

/**
 * Meneruskan request JSON ke trace-api memakai cookie sesi (X-Trace-Session)
 * plus header konteks lama untuk mode demo. Status dan body upstream
 * dikembalikan apa adanya agar kontrak API tetap satu sumber.
 */
export async function proxyJson(path: string, options: ProxyOptions = {}): Promise<NextResponse> {
  const [context, token] = await Promise.all([readOperatorContext(), readSessionToken()]);
  if (options.requireOperator && !context && !token) {
    return NextResponse.json({ error: OPERATOR_REQUIRED_MESSAGE }, { status: 401 });
  }

  const admin = options.admin ? await isAdminSession() : false;

  try {
    const response = await callTraceApi(path, {
      method: options.method ?? 'GET',
      body: options.body,
      context,
      admin,
      sessionToken: token,
      contextVersion: options.contextVersion ?? null,
      query: options.query,
    });
    return passthrough(response, await response.text());
  } catch {
    return upstreamError();
  }
}

/** Ambil versi konteks dari header x-trace-context-version bawaan browser (nullable). */
export function readContextVersion(request: Request): number | null {
  const raw = request.headers.get('x-trace-context-version');
  const parsed = raw ? Number.parseInt(raw, 10) : NaN;
  return Number.isFinite(parsed) ? parsed : null;
}

/** Khusus unduhan CSV: header content-disposition ikut diteruskan. */
export async function proxyFile(
  path: string,
  query?: Record<string, string | number | undefined>,
): Promise<NextResponse> {
  const context = await readOperatorContext();

  try {
    const response = await callTraceApi(path, { context, query });
    const headers = new Headers();
    headers.set('content-type', response.headers.get('content-type') ?? 'text/csv; charset=utf-8');
    headers.set('cache-control', 'no-store');
    const disposition = response.headers.get('content-disposition');
    if (disposition) headers.set('content-disposition', disposition);
    return new NextResponse(response.body, { status: response.status, headers });
  } catch {
    return upstreamError();
  }
}
