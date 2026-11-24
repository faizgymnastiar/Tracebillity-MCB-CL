import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Akhiri sesi produksi aktif. */
export async function DELETE(): Promise<NextResponse> {
  return proxyJson('/api/v1/production/end', { method: 'DELETE', requireOperator: true });
}
