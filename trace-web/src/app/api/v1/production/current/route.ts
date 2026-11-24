import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Konteks produksi aktif milik akun login (atau penanda belum mulai). */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/production/current', { requireOperator: true });
}
