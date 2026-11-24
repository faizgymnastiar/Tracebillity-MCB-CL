import { LoginBrandPanel } from '@/components/auth/LoginBrandPanel';
import type { PublicConfig } from '@/types';

interface LoginWorkspaceProps {
  config: PublicConfig;
  forceChange: string | null;
  rememberedEmail: string;
  error: string;
}

export function LoginWorkspace({ config, forceChange, rememberedEmail, error }: LoginWorkspaceProps) {
  return (
    <main className="login-page">
      <LoginBrandPanel config={config} />

      <section className="login-panel">
        <div className="login-card">
          <div className="login-card-head">
            <h2>{forceChange ? 'Ganti Kata Sandi' : 'Login Operator'}</h2>
            <p>
              {forceChange
                ? `Akun ${forceChange} wajib memakai kata sandi baru sebelum melanjutkan.`
                : 'Masukkan email dan kata sandi akun produksi Anda.'}
            </p>
          </div>

          {error ? <div className="scan-alert error">{error}</div> : null}

          {forceChange ? (
            <form method="post" action="/api/v1/auth/change-password" className="login-form">
              <div className="login-field">
                <label className="login-label" htmlFor="login-current">Kata Sandi Saat Ini</label>
                <input
                  id="login-current" name="currentPassword"
                  className="login-input"
                  type="password"
                  autoComplete="current-password"
                  required
                />
              </div>
              <div className="login-field">
                <label className="login-label" htmlFor="login-new">Kata Sandi Baru (min. 8 karakter)</label>
                <input
                  id="login-new" name="newPassword"
                  className="login-input"
                  type="password"
                  autoComplete="new-password"
                  required
                />
              </div>
              <div className="login-field">
                <label className="login-label" htmlFor="login-confirm">Ulangi Kata Sandi Baru</label>
                <input
                  id="login-confirm" name="confirmPassword"
                  className="login-input"
                  type="password"
                  autoComplete="new-password"
                  required
                />
              </div>
              <button type="submit" className="login-button">
                GANTI KATA SANDI
                <span aria-hidden="true">→</span>
              </button>
            </form>
          ) : (
            <form method="post" action="/api/v1/auth/login" className="login-form">
              <div className="login-field">
                <label className="login-label" htmlFor="login-email">Email Akun</label>
                <input
                  id="login-email" name="email" defaultValue={rememberedEmail}
                  className="login-input"
                  type="email"
                  placeholder="operator@lelco.example"
                  autoComplete="username"
                  required
                />
              </div>

              <div className="login-field">
                <label className="login-label" htmlFor="login-password">Kata Sandi</label>
                <input
                  id="login-password" name="password"
                  className="login-input"
                  type="password"
                  autoComplete="current-password"
                  required
                />
              </div>

              <label className="login-remember">
                <input
                  type="checkbox" name="remember" defaultChecked={Boolean(rememberedEmail)}
                />
                Ingat email saya
              </label>

              <button type="submit" className="login-button">
                {config.loginSubmitText}
                <span aria-hidden="true">→</span>
              </button>
            </form>
          )}

          <div className="login-support">
            Belum punya akun? Hubungi Super Admin untuk pembuatan akun &amp; hak operasi.
            <br />
            <span>Kendala akses atau indikator tidak terhubung? Hubungi IT &amp; Engineering Ext: 4401 / 4402</span>
          </div>
        </div>
      </section>
    </main>
  );
}
