import type { PublicConfig } from '@/types';

interface LoginBrandPanelProps {
  config: PublicConfig;
}

export function LoginBrandPanel({ config }: LoginBrandPanelProps) {
  return (
    <section className="login-brand">
      <div className="login-grid" aria-hidden="true" />
      <div className="login-glow" aria-hidden="true" />
      <svg className="login-blueprint" fill="none" viewBox="0 0 400 400" aria-hidden="true">
        <circle cx="200" cy="200" r="180" stroke="currentColor" strokeWidth="1.5" strokeDasharray="6 6" />
        <circle cx="200" cy="200" r="140" stroke="currentColor" />
        <circle cx="200" cy="200" r="100" stroke="currentColor" strokeWidth="1.5" strokeDasharray="4 4" />
        <circle cx="200" cy="200" r="60" stroke="currentColor" />
        <path d="M20 200h360M200 20v360" stroke="currentColor" strokeWidth="0.8" />
      </svg>

      <div className="login-brand-body">
        <img className="login-logo" src="/LELCO Manufacturing White Logo.png" alt="LELCO Manufacturing" />
        <h1>{config.loginTitle}</h1>
        <p>{config.subtitle}</p>
      </div>

      <footer className="login-brand-footer">{config.footerTopText}</footer>
    </section>
  );
}
