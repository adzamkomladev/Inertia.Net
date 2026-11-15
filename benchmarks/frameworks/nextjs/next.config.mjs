// output: 'standalone' is what the Next.js docs recommend for Docker. BENCH_MODE (ssr or spa) is fixed at build time.
const nextConfig = {
  output: 'standalone',
  env: { BENCH_MODE: process.env.BENCH_MODE ?? 'ssr' },
}

export default nextConfig
