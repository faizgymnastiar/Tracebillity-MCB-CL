import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Rekonsiliasi sequence setelah pemulihan backup — generate berhenti sampai batas tertinggi sah. */
export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/labels/sequence/reconcile', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
  });
}
