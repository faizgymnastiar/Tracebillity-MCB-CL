import { NextResponse } from 'next/server';
import type { AuthProfile, ProductionContext } from '@/types';
import { getTraceSession } from '@/server/session';

export const dynamic = 'force-dynamic';

export async function GET(): Promise<NextResponse<{
  profile: AuthProfile;
  production: ProductionContext | null;
  mustChangePassword: boolean;
  hak: string[];
}>> {
  const session = await getTraceSession();
  return NextResponse.json({
    profile: session.profile,
    production: session.production,
    mustChangePassword: session.remote?.mustChangePassword ?? false,
    hak: session.remote?.permissions ?? [],
  });
}
