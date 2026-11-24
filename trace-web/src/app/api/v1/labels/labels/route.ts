import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Label per batch/status dengan pagination — stok bersifat lintas WO. */
export async function GET(request: Request): Promise<NextResponse> {
  const { searchParams } = new URL(request.url);
  return proxyJson('/api/v1/labels/labels', {
    requireOperator: true,
    query: {
      batchId: searchParams.get('batchId') ?? '',
      status: searchParams.get('status') ?? '',
      page: searchParams.get('page') ?? '1',
      pageSize: searchParams.get('pageSize') ?? '50',
    },
  });
}
