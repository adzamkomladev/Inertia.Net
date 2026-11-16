// The TanStack Start docs' Node.js setup (Vite + the nitro/vite plugin). BENCH_MODE=spa turns on Start's SPA mode at build time.
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import viteReact from '@vitejs/plugin-react'
import { nitro } from 'nitro/vite'
import { defineConfig } from 'vite'

const spa = process.env.BENCH_MODE === 'spa'
// BENCH_TUNED=1: Nitro route rules cache whole responses in memory, set like Inertia.Net tuned: pages for 60 s and todo lists
// for 10 s, each served stale while it refreshes. Only GET is cached; compute is left uncached, as in every tuned app.
const page = { cache: { maxAge: 60, swr: true, staleMaxAge: 300 } }
// Nitro 3 leaves the query string out of the cache key unless allowQuery names it.
const list = { cache: { maxAge: 10, swr: true, staleMaxAge: 60, allowQuery: ['page', 'perPage'] } }
const routeRules: Record<string, typeof page | typeof list> = process.env.BENCH_TUNED === '1'
  ? { '/': page, '/dashboard': page, '/todos/new': page, '/todos': list, '/api/home': page, '/api/dashboard': page, '/api/todos': list }
  : {}

export default defineConfig({
  define: { __BENCH_SPA__: JSON.stringify(spa) },
  server: { host: '0.0.0.0', port: 8080 },
  plugins: [tanstackStart({ spa: { enabled: spa } }), viteReact(), nitro({ cache: { driver: 'kv' }, plugins: ['./server/redis.ts'], routeRules })],
})
