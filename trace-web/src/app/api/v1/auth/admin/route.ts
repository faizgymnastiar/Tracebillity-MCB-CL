import { NextResponse } from 'next/server';
import { verifyAdminPin } from '@/server/trace-backend';
import {
  clearAdminSession,
  isAdminSession,
  readSessionToken,
  writeAdminSession,
} from '@/server/session';

export const dynamic = 'force-dynamic';

interface AdminGateBody {
  pin?: string;
}

export async function GET(): Promise<NextResponse<{ admin: boolean }>> {
  return NextResponse.json({ admin: await isAdminSession() });
}

/**
 * PIN studio konfigurasi (warisan). Saat enforcement aktif, backend juga
 * menuntut sesi login dengan hak master — PIN saja tidak lagi cukup.
 */
export async function POST(
  request: Request,
): Promise<NextResponse<{ ok: boolean; admin: boolean; error?: string }>> {
  let body: AdminGateBody;
  try {
    body = (await request.json()) as AdminGateBody;
  } catch {
    return NextResponse.json({ ok: false, admin: false, error: 'Payload PIN tidak valid.' }, { status: 400 });
  }

  const token = await readSessionToken();
  if (!(await verifyAdminPin(body.pin ?? '', token))) {
    return NextResponse.json(
      { ok: false, admin: false, error: 'PIN Super Admin salah atau akun tidak memiliki hak admin.' },
      { status: 401 },
    );
  }

  await writeAdminSession();
  return NextResponse.json({ ok: true, admin: true });
}

export async function DELETE(): Promise<NextResponse<{ ok: boolean }>> {
  await clearAdminSession();
  return NextResponse.json({ ok: true });
}
