import { NextResponse } from 'next/server';
import { proxyJson, readContextVersion } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

/** Catat NG pertama / NG ulang; wajib kunci idempotensi agar retry tidak menggandakan record. */
export async function POST(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/ng/records/ng', {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
    contextVersion: readContextVersion(request),
  });
}
