import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Daftar sesi login — admin melihat semua, pengguna biasa hanya miliknya. */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/auth/sessions', { requireOperator: true });
}
