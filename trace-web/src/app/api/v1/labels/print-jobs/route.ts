import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Riwayat job cetak (filter batch/status/tanggal + pagination) dan pembuatan job baru. */
export async function GET(request: Request): Promise<NextResponse> {
  const { searchParams } = new URL(request.url);
  return proxyJson('/api/v1/labels/print-jobs', {
    requireOperator: true,
    query: {
      batchId: searchParams.get('batchId') ?? '',
      status: searchParams.get('status') ?? '',
      from: searchParams.get('from') ?? '',
      to: searchParams.get('to') ?? '',
      page: searchParams.get('page') ?? '1',
      pageSize: searchParams.get('pageSize') ?? '25',
    },
  });
}

export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/labels/print-jobs', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
  });
}
