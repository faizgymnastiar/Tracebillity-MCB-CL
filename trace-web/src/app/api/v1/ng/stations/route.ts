import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Pos QC aktif + defect keluarga SKU pelaku (dropdown server-side). */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/ng/stations', { requireOperator: true });
}
