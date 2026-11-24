import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

export async function PUT(request: Request): Promise<NextResponse> {
  return proxyJson('/api/v1/auth/change-password', {
    method: 'PUT',
    body: await request.text(),
    requireOperator: true,
  });
}

export async function POST(request: Request): Promise<NextResponse> {
  const fetchSite = request.headers.get('sec-fetch-site');
  if (fetchSite && fetchSite !== 'same-origin' && fetchSite !== 'none') {
    return NextResponse.json({ error: 'Buka halaman ganti kata sandi dari aplikasi ini.' }, { status: 403 });
  }
  const redirect = (location: string) => new NextResponse(null, { status: 303, headers: { location } });
  const fields = await request.formData().catch(() => null);
  const currentPassword = String(fields?.get('currentPassword') ?? '');
  const newPassword = String(fields?.get('newPassword') ?? '');
  if (newPassword.length < 8 || newPassword !== fields?.get('confirmPassword')) {
    return redirect('/login?error=confirm');
  }
  const response = await proxyJson('/api/v1/auth/change-password', {
    method: 'PUT',
    body: JSON.stringify({ currentPassword, newPassword }),
    requireOperator: true,
  });
  return redirect(response.ok ? '/' : '/login?error=change');
}
