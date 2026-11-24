import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function GET(request: Request): Promise<NextResponse> {
  const { searchParams } = new URL(request.url);
  return proxyJson('/api/v1/traceability/records', {
    query: {
      search: searchParams.get('search') ?? '',
      date: searchParams.get('date') ?? '',
      status: searchParams.get('status') ?? 'ALL',
      page: searchParams.get('page') ?? '1',
      pageSize: searchParams.get('pageSize') ?? '50',
    },
  });
}
