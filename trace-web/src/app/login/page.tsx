import { redirect } from 'next/navigation';
import { cookies } from 'next/headers';
import { ApiOfflineNotice } from '@/components/ui/ApiOfflineNotice';
import { LoginBrandPanel } from '@/components/auth/LoginBrandPanel';
import { LoginWorkspace } from '@/components/auth/LoginWorkspace';
import { TRACE_API_ORIGIN, getPublicConfig, isTraceApiReachable } from '@/server/trace-backend';
import { getTraceSession } from '@/server/session';

export const dynamic = 'force-dynamic';

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ error?: string }> }) {
  const [session, config, online, jar, query] = await Promise.all([
    getTraceSession(),
    getPublicConfig(),
    isTraceApiReachable(),
    cookies(),
    searchParams,
  ]);
  const forceChange = session.remote?.mustChangePassword ? session.remote.email : null;
  if (session.profile.authenticated && !forceChange) redirect('/');

  if (!online) {
    return (
      <main className="login-page">
        <LoginBrandPanel config={config} />
        <section className="login-panel">
          <ApiOfflineNotice origin={TRACE_API_ORIGIN} />
        </section>
      </main>
    );
  }

  const errors: Record<string, string> = {
    login: 'Login gagal. Periksa email dan kata sandi.',
    change: 'Ganti kata sandi gagal. Periksa kata sandi saat ini dan coba lagi.',
    confirm: 'Kata sandi baru minimal 8 karakter dan kedua isian harus sama.',
  };
  return <LoginWorkspace config={config} forceChange={forceChange}
    rememberedEmail={jar.get('trace_login_email')?.value ?? ''} error={query.error ? errors[query.error] ?? '' : ''} />;
}
