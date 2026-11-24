import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Daftar unit karantina/rework/OK dengan pencarian + pagination. */
export async function GET(request: Request): Promise<NextResponse> {
  const { searchParams } = new URL(request.url);
  return proxyJson('/api/v1/ng/units', {
    requireOperator: true,
    query: {
      search: searchParams.get('search') ?? '',
      status: searchParams.get('status') ?? '',
      page: searchParams.get('page') ?? '1',
      pageSize: searchParams.get('pageSize') ?? '25',
    },
  });
}
