// BENCH_MODE=spa turns SSR off at build time (Nuxt's client-only mode); the Nitro server then answers page requests with the SPA shell.
export default defineNuxtConfig({
  compatibilityDate: '2026-11-01',
  ssr: process.env.BENCH_MODE !== 'spa',
  devtools: { enabled: false },
  telemetry: false,
  devServer: { host: '0.0.0.0', port: 8080 },
})
