import type { ReactNode } from 'react';
import { cn } from '@/lib/cn';

export type Tone = 'ok' | 'ng' | 'rework' | 'neutral' | 'info';

const TONE_TEXT: Record<Tone, string | undefined> = {
  ok: '#0b6b46',
  ng: '#a30f2d',
  rework: '#a34f00',
  neutral: undefined,
  info: undefined,
};

const BADGE_CLASS: Record<Tone, string> = {
  ok: 'ok',
  ng: 'ng',
  rework: 'rw',
  neutral: 'neutral',
  info: 'neutral',
};

interface PageHeaderProps {
  eyebrow: string;
  title: string;
  description?: string;
  actions?: ReactNode;
}

export function PageHeader({ eyebrow, title, description, actions }: PageHeaderProps) {
  return (
    <div className="erp-page-head">
      <div>
        <div className="eyebrow">{eyebrow}</div>
        <h1>{title}</h1>
        {description ? <p>{description}</p> : null}
      </div>
      {actions ? <div className="erp-page-actions">{actions}</div> : null}
    </div>
  );
}

interface KpiCardProps {
  label: string;
  value: ReactNode;
  meta?: string;
  tone?: Tone;
  highlight?: boolean;
}

export function KpiCard({ label, value, meta, tone = 'neutral', highlight = false }: KpiCardProps) {
  return (
    <div className={cn('kpi', highlight && 'kpi-highlight')}>
      <span className="label">{label}</span>
      <strong style={{ color: TONE_TEXT[tone] }}>{value}</strong>
      {meta ? <p className="meta">{meta}</p> : null}
    </div>
  );
}

interface StatusBadgeProps {
  tone?: Tone;
  children: ReactNode;
}

export function StatusBadge({ tone = 'neutral', children }: StatusBadgeProps) {
  return <span className={cn('badge', BADGE_CLASS[tone])}>{children}</span>;
}

interface PanelProps {
  title?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
  id?: string;
}

export function Panel({ title, actions, children, className, id }: PanelProps) {
  return (
    <section className={cn('panel', className)} id={id}>
      {title || actions ? (
        <header className="panel-head">
          {title ? <h2 className="panel-title">{title}</h2> : <span />}
          {actions ? <div className="panel-actions">{actions}</div> : null}
        </header>
      ) : null}
      {children}
    </section>
  );
}

interface EmptyStateProps {
  children: ReactNode;
}

export function EmptyState({ children }: EmptyStateProps) {
  return <p className="helper" style={{ textAlign: 'center', padding: '22px 8px' }}>{children}</p>;
}

interface NoticeProps {
  tone?: 'ok' | 'error' | 'rework' | 'neutral';
  children: ReactNode;
}

export function Notice({ tone = 'neutral', children }: NoticeProps) {
  const style =
    tone === 'ok'
      ? { borderLeftColor: '#059669', background: '#eafaf1' }
      : tone === 'error'
        ? { borderLeftColor: '#e11d48', background: '#fdeef1' }
        : tone === 'rework'
          ? { borderLeftColor: '#ff7f00', background: '#fff5e8' }
          : undefined;
  return (
    <div className="notice" style={style}>
      {children}
    </div>
  );
}
