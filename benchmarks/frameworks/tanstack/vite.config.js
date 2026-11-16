// The TanStack Start docs' Node.js setup (Vite + the nitro/vite plugin). BENCH_MODE=spa turns on Start's SPA mode at build time.
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import viteReact from '@vitejs/plugin-react'
import { nitro } from 'nitro/vite'
import { defineConfig } from 'vite'

const spa = process.env.BENCH_MODE === 'spa'

export default defineConfig({
  define: { __BENCH_SPA__: JSON.stringify(spa) },
  server: { host: '0.0.0.0', port: 8080 },
  plugins: [tanstackStart({ spa: { enabled: spa } }), viteReact(), nitro()],
})
