import { NextResponse } from 'next/server';
import { proxyJson, readContextVersion } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** OK akhir mode serial saja atau pasangan label + serial hasil rework. */
export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/ng/records/ok', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
    contextVersion: readContextVersion(request),
  });
}
