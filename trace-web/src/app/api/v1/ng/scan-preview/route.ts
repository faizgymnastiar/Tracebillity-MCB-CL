import { callTraceApi } from '@/server/trace-backend';
import { readSessionToken } from '@/server/session';

export async function POST(request: Request): Promise<Response> {
  const sessionToken = await readSessionToken();
  if (!sessionToken) return new Response(null, { status: 401 });
  try {
    const upstream = await callTraceApi('/api/v1/ng/scan-preview', {
      sessionToken, method: 'POST', body: await request.text(),
    });
    return new Response(upstream.body, {
      status: upstream.status,
      headers: {
        'content-type': upstream.headers.get('content-type') ?? 'text/plain',
        'cache-control': 'no-store',
      },
    });
  } catch {
    return new Response(null, { status: 502 });
  }
}
