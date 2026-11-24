import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Manajemen akun & hak operasi — backend menolak tanpa izin admin.accounts. */
export async function GET(): Promise<NextResponse> {
  return proxyJson('/api/v1/accounts', { requireOperator: true });
}

export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/accounts', { method: 'POST', body: await request.text(), requireOperator: true });
}

export async function PUT(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/accounts', { method: 'PUT', body: await request.text(), requireOperator: true });
}
