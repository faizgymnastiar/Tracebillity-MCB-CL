import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Akhiri (revoke) satu sesi login — milik sendiri atau semua sesi bila admin. */
export async function POST(
  _request: Request,
  context: { params: Promise<{ id: string }> },
): Promise<NextResponse> {
  const { id } = await context.params;
  return proxyJson(`/api/v1/auth/sessions/${encodeURIComponent(id)}/revoke`, {
    method: 'POST',
    requireOperator: true,
  });
}
