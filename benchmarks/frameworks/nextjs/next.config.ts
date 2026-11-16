import type { NextConfig } from 'next'

// output: 'standalone' is what the Next.js docs recommend for Docker. BENCH_MODE (ssr or spa) is fixed at build time.
const nextConfig: NextConfig = {
  output: 'standalone',
  env: { BENCH_MODE: process.env.BENCH_MODE ?? 'ssr', BENCH_TUNED: process.env.BENCH_TUNED ?? '0' },
  // 'use cache: remote' (the dashboard) keeps its entries in Redis, where every app of the benchmark caches.
  cacheComponents: true,
  cacheHandlers: { remote: require.resolve('./lib/redis-cache-handler.mts') },
}

export default nextConfig
