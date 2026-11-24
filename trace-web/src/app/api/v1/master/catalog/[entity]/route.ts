import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

const ALLOWED_ENTITIES = ['family', 'sku', 'shift', 'line', 'stage', 'defect'];

/** Tambah/perbarui satu entitas master — backend menuntut hak admin.master. */
export async function POST(
  request: Request,
  context: { params: Promise<{ entity: string }> },
): Promise<NextResponse> {
  const { entity } = await context.params;
  if (!ALLOWED_ENTITIES.includes(entity)) {
    return NextResponse.json({ error: `Entitas master "${entity}" tidak dikenal.` }, { status: 400 });
  }
  return proxyJson(`/api/v1/master/catalog/${entity}`, {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
  });
}
