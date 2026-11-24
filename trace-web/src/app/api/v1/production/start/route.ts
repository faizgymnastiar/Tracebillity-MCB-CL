import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Mulai sesi produksi (shift/line/SKU + daftar WO) dari modal Mulai produksi. */
export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/production/start', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
  });
}
