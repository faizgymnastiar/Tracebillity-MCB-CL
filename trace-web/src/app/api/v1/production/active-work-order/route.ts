import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Ganti WO aktif; versi konteks naik sehingga layar berkonteks lama ditolak. */
export async function PUT(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/production/active-work-order', {
    method: 'PUT',
    body: await request.text(),
    requireOperator: true,
  });
}
