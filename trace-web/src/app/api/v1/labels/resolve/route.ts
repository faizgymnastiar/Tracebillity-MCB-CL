import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Bukti cetak balik: payload/ID hasil scan → record label di DB. */
export async function GET(request: Request): Promise<NextResponse> {
  const { searchParams } = new URL(request.url);
  return proxyJson('/api/v1/labels/resolve', {
    requireOperator: true,
    query: { value: searchParams.get('value') ?? '' },
  });
}
