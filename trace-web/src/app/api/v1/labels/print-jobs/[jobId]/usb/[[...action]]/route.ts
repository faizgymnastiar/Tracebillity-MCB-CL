import { proxyJson } from '@/server/trace-proxy';

type Context = { params: Promise<{ jobId: string; action?: string[] }> };

export async function GET(_request: Request, context: Context) {
  const { jobId, action } = await context.params;
  if (action?.length) return new Response(null, { status: 404 });
  return proxyJson(`/api/v1/labels/print-jobs/${encodeURIComponent(jobId)}/usb`, { requireOperator: true });
}

export async function POST(request: Request, context: Context) {
  const { jobId, action } = await context.params;
  const step = action?.join('/');
  if (step !== 'claim' && step !== 'report') return new Response(null, { status: 404 });
  return proxyJson(`/api/v1/labels/print-jobs/${encodeURIComponent(jobId)}/usb/${step}`, {
    method: 'POST', requireOperator: true, body: await request.text(),
  });
}
