import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Riwayat satu unit: kejadian NG berurutan, rework per percobaan, dan hasil OK. */
export async function GET(
  request: Request,
  context: { params: Promise<{ unitRef: string }> },
): Promise<NextResponse> {
  const { unitRef } = await context.params;
  return proxyJson(`/api/v1/ng/units/${encodeURIComponent(unitRef)}`, { requireOperator: true });
}
