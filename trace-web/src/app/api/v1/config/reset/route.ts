import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function POST(): Promise<NextResponse> {
  return proxyJson('/api/v1/config/reset', { method: 'POST', admin: true });
}
