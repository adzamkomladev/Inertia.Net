// BENCH_MODE=spa turns SSR off at build time (Nuxt's client-only mode); the Nitro server then answers page requests with the SPA shell.
// BENCH_TUNED=1: Nitro route rules cache whole responses in memory, set like Inertia.Net tuned: pages for 60 s and todo lists
// for 10 s, each served stale while it refreshes. Only GET is cached; compute is left uncached, as in every tuned app.
const page = { cache: { maxAge: 60, swr: true, staleMaxAge: 300 } }
const list = { cache: { maxAge: 10, swr: true, staleMaxAge: 60 } }
const routeRules: Record<string, typeof page> = process.env.BENCH_TUNED === '1'
  ? { '/': page, '/dashboard': page, '/todos/new': page, '/todos': list, '/api/home': page, '/api/dashboard': page, '/api/todos': list }
  : {}

export default defineNuxtConfig({
  routeRules,
  compatibilityDate: '2026-11-01',
  ssr: process.env.BENCH_MODE !== 'spa',
  devtools: { enabled: false },
  telemetry: false,
  devServer: { host: '0.0.0.0', port: 8080 },
})
