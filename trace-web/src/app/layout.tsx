import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import './globals.css';
import { getPublicConfig } from '@/server/trace-backend';

const ICONS: Metadata['icons'] = {
  icon: [
    { url: '/favicon.svg', type: 'image/svg+xml' },
    { url: '/favicon.ico' },
  ],
  apple: '/apple-touch-icon.png',
};

export async function generateMetadata(): Promise<Metadata> {
  try {
    const config = await getPublicConfig();
    return {
      title: config.browserTitle,
      description: `${config.companyName} — ${config.subtitle}`,
      icons: ICONS,
    };
  } catch {
    return {
      title: 'LELCO Traceability MCB CL',
      description: 'Traceability produksi MCB CL — LELCO',
      icons: ICONS,
    };
  }
}

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="id">
      <body>{children}</body>
    </html>
  );
}
