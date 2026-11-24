import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Operator mencatat hasil cetak fisik; tanpa langkah ini label tetap belum dianggap tercetak. */
export async function POST(
  request: Request,
  context: { params: Promise<{ jobId: string }> },
): Promise<NextResponse> {
  const { jobId } = await context.params;
  return proxyJson(`/api/v1/labels/print-jobs/${encodeURIComponent(jobId)}/confirm`, {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
  });
}
