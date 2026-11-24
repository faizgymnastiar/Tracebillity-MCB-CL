import { NextResponse } from 'next/server';
import { proxyJson, readContextVersion } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function GET(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/traceability/scan-state', { contextVersion: readContextVersion(request) });
}

export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/traceability/scan-state', {
    method: 'POST',
    body: await request.text(),
    contextVersion: readContextVersion(request),
    requireOperator: true,
  });
}

export async function DELETE(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/traceability/scan-state', {
    method: 'DELETE',
    contextVersion: readContextVersion(request),
    requireOperator: true,
  });
}
