import { NextResponse } from 'next/server';
import { proxyFile } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Unduhan CSV (ID + payload) satu job — kolom tetap sama untuk cetak pertama maupun ulang. */
export async function GET(
  _request: Request,
  context: { params: Promise<{ jobId: string }> },
): Promise<NextResponse> {
  const { jobId } = await context.params;
  return proxyFile(`/api/v1/labels/print-jobs/${encodeURIComponent(jobId)}/export.csv`);
}
