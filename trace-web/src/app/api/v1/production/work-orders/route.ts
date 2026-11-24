import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Tambah WO baru ke sesi produksi yang sedang berjalan. */
export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/production/work-orders', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
  });
}
