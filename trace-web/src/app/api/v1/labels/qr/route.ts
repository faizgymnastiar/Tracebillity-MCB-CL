import { callTraceApi } from '@/server/trace-backend';
import { readSessionToken } from '@/server/session';

export async function GET(request: Request): Promise<Response> {
  const sessionToken = await readSessionToken();
  if (!sessionToken) return new Response('Silakan login.', { status: 401 });
  const value = new URL(request.url).searchParams.get('value');
  if (!value) return new Response('ID label wajib diisi.', { status: 400 });
  const upstream = await callTraceApi('/api/v1/labels/qr.png', { sessionToken, query: { value } });
  return new Response(upstream.body, {
    status: upstream.status,
    headers: {
      'content-type': upstream.headers.get('content-type') ?? 'text/plain',
      'cache-control': 'no-store',
    },
  });
}
