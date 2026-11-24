import { NextResponse } from 'next/server';
import { proxyJson } from '@/server/trace-proxy';

export const dynamic = 'force-dynamic';

const ALLOWED_ENTITIES = ['family', 'sku', 'shift', 'line', 'stage', 'defect'];

/** Nonaktifkan/aktifkan master — hanya memengaruhi pilihan baru. */
export async function PUT(
  request: Request,
  context: { params: Promise<{ entity: string; id: string }> },
): Promise<NextResponse> {
  const { entity, id } = await context.params;
  if (!ALLOWED_ENTITIES.includes(entity)) {
    return NextResponse.json({ error: `Entitas master "${entity}" tidak dikenal.` }, { status: 400 });
  }
  return proxyJson(`/api/v1/master/catalog/${entity}/${encodeURIComponent(id)}/status`, {
    method: 'PUT',
    body: await request.text(),
    requireOperator: true,
  });
}
