import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Ringkasan stok label internal NG (Fase 2). */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/labels', { requireOperator: true });
}
