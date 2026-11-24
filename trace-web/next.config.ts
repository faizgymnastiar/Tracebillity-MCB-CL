import type { NextConfig } from 'next';

/**
 * Frontend tidak memakai rewrite ke backend .NET.
 * Route handler `src/app/api/v1/**` mem-proxy ke `TRACE_API_ORIGIN`
 * sambil menerjemahkan cookie sesi menjadi header operator/admin.
 */
const nextConfig: NextConfig = {
  allowedDevOrigins: ['127.0.0.1'],
  reactStrictMode: true,
  poweredByHeader: false,
};

export default nextConfig;
