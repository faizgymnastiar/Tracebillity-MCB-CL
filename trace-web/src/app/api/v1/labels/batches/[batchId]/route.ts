import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Rincian satu batch: daftar ID/payload + riwayat job cetaknya. */
export async function GET(
  request: Request,
  context: { params: Promise<{ batchId: string }> },
): Promise<NextResponse> {
  const { batchId } = await context.params;
  const { searchParams } = new URL(request.url);
  return proxyJson(`/api/v1/labels/batches/${encodeURIComponent(batchId)}`, {
    requireOperator: true,
    query: { labelLimit: searchParams.get('labelLimit') ?? '' },
  });
}
