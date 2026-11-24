import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/config/public');
}

export async function PUT(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/config', { method: 'PUT', body: await request.text(), admin: true });
}
