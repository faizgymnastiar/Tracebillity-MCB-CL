import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Ringkasan karantina, Uji ulang, Tahan QC, dan OK akhir hari ini (WIB). */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/ng/summary', { requireOperator: true });
}
