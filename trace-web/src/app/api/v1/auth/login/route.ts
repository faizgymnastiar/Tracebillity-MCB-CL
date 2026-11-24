import { NextResponse } from 'next/server';
import { cookies } from 'next/headers';
import { loginWithPassword } from '@/server/trace-backend';
import { clearOperatorSession, writeSessionToken } from '@/server/session';

export const dynamic = 'force-dynamic';

interface LoginBody {
  email?: string;
  password?: string;
}

/**
 * Login fondasi Fase 1: email + kata sandi diverifikasi trace-api (hash framework).
 * Token sesi server-side disimpan di cookie httpOnly; browser tidak pernah melihatnya.
 */
export async function POST(request: Request): Promise<NextResponse> {
  const isForm = request.headers.get('content-type')?.startsWith('application/x-www-form-urlencoded');
  const fetchSite = request.headers.get('sec-fetch-site');
  if (isForm && fetchSite && fetchSite !== 'same-origin' && fetchSite !== 'none') {
    return NextResponse.json({ error: 'Buka halaman login dari aplikasi ini.' }, { status: 403 });
  }
  const formRedirect = (location: string) => new NextResponse(null, { status: 303, headers: { location } });
  let body: LoginBody;
  let remember = false;
  try {
    if (isForm) {
      const fields = await request.formData();
      body = { email: String(fields.get('email') ?? ''), password: String(fields.get('password') ?? '') };
      remember = fields.has('remember');
    } else {
      body = (await request.json()) as LoginBody;
    }
  } catch {
    if (isForm) return formRedirect('/login?error=login');
    return NextResponse.json({ error: 'Payload login tidak valid.' }, { status: 400 });
  }

  const email = String(body.email ?? '').trim();
  const password = String(body.password ?? '');
  if (!email || !password) {
    if (isForm) return formRedirect('/login?error=login');
    return NextResponse.json({ error: 'Email dan kata sandi wajib diisi.' }, { status: 400 });
  }

  const outcome = await loginWithPassword(email, password);
  if (!outcome.ok) {
    if (isForm) return formRedirect('/login?error=login');
    return NextResponse.json({ error: outcome.error, code: outcome.code }, { status: outcome.status });
  }

  await writeSessionToken(outcome.result.token);
  await clearOperatorSession();
  if (isForm) {
    const jar = await cookies();
    if (remember) jar.set('trace_login_email', email, { path: '/', httpOnly: true, sameSite: 'lax', maxAge: 60 * 60 * 24 * 30 });
    else jar.delete('trace_login_email');
    return formRedirect(outcome.result.profile.mustChangePassword ? '/login' : '/');
  }

  return NextResponse.json({
    profile: outcome.result.profile,
    expiresAtUtc: outcome.result.expiresAtUtc,
    mustChangePassword: outcome.result.profile.mustChangePassword,
  });
}
