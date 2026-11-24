import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Pilihan master aktif untuk pemilih "Mulai produksi" dan layar NG. */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/master/catalog/active', { requireOperator: true });
}
