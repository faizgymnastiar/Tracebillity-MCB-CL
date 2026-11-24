import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Katalog master lengkap (termasuk nonaktif) — butuh hak admin.master di backend. */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/master/catalog', { requireOperator: true });
}
