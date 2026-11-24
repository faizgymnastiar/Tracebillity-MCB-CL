import { NextResponse } from 'next/server';
import { logoutSession } from '@/server/trace-backend';
import {
  clearAdminSession,
  clearOperatorSession,
  clearSessionToken,
  readSessionToken,
} from '@/server/session';

export const dynamic = 'force-dynamic';

/** Akhiri sesi: token direvoke di backend lalu semua cookie sesi dibersihkan. */
export async function POST(): Promise<NextResponse<{ ok: boolean }>> {
  const token = await readSessionToken();
  await logoutSession(token);
  await clearSessionToken();
  await clearOperatorSession();
  await clearAdminSession();
  return NextResponse.json({ ok: true });
}
