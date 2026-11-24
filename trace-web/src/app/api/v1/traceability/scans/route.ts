import { NextResponse } from 'next/server';
import { proxyJson, readContextVersion } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/traceability/scans', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
    contextVersion: readContextVersion(request),
  });
}
