import { NextResponse } from 'next/server';
import { proxyFile } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function GET(request: Request): Promise<NextResponse> {
  const { searchParams } = new URL(request.url);
  return proxyFile('/api/v1/traceability/export', {
    search: searchParams.get('search') ?? '',
    date: searchParams.get('date') ?? '',
    status: searchParams.get('status') ?? 'ALL',
    columns: searchParams.get('columns') ?? '',
  });
}
