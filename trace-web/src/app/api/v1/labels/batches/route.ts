import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Daftar batch label dengan filter tanggal + pagination. */
export async function GET(request: Request): Promise<NextResponse> {
  const { searchParams } = new URL(request.url);
  return proxyJson('/api/v1/labels/batches', {
    requireOperator: true,
    query: {
      search: searchParams.get('search') ?? '',
      from: searchParams.get('from') ?? '',
      to: searchParams.get('to') ?? '',
      page: searchParams.get('page') ?? '1',
      pageSize: searchParams.get('pageSize') ?? '25',
    },
  });
}

/** Generate batch stok label (idempoten; backend menolak tanpa hak print.manage). */
export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/labels/batches', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
  });
}
