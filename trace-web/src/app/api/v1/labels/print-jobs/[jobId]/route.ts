import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function GET(
  _request: Request,
  context: { params: Promise<{ jobId: string }> },
): Promise<NextResponse> {
  const { jobId } = await context.params;
  return proxyJson(`/api/v1/labels/print-jobs/${encodeURIComponent(jobId)}`, { requireOperator: true });
}
