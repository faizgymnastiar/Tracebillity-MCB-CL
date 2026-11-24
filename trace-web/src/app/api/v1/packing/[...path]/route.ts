import { proxyJson, readContextVersion } from '@/server/trace-proxy';

type RouteContext = { params: Promise<{ path: string[] }> };

export async function GET(request: Request, context: RouteContext) {
  const { path } = await context.params;
  const route = path.join('/');
  if (!['history', 'history/unit', 'work-order'].includes(route)) return new Response(null, { status: 404 });
  return proxyJson(`/api/v1/packing/${route}`, {
    requireOperator: true,
    query: Object.fromEntries(new URL(request.url).searchParams),
  });
}

export async function POST(request: Request, context: RouteContext) {
  const { path } = await context.params;
  const route = path.join('/');
  if (!/^(inner|outer)\/(open|add|close)$/.test(route)) return new Response(null, { status: 404 });
  return proxyJson(`/api/v1/packing/${route}`, {
    method: 'POST',
    body: await request.text(),
    requireOperator: true,
    contextVersion: readContextVersion(request),
  });
}
