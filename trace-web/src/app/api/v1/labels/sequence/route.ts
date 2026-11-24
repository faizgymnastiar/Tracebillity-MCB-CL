import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Kondisi sequence penerbitan label (next_value vs batas tertinggi yang pernah keluar). */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/labels/sequence', { requireOperator: true });
}
