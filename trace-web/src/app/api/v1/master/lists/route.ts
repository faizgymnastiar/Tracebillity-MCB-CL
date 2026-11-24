import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/master/lists');
}

export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/master/lists', { method: 'POST', body: await request.text(), admin: true });
}

export async function DELETE(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/master/lists', { method: 'DELETE', body: await request.text(), admin: true });
}
