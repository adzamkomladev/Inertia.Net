import fs from 'node:fs'
import path from 'node:path'
import inertia from '@inertiajs/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig, type Plugin } from 'vite'

// Inertia.Net finds the dev server through a "hot file" (wwwroot/hot) holding its origin, like laravel-vite-plugin does.
// @inertiajs/vite does not write one, so this writes it while the dev server runs.
function hotFile(): Plugin {
  const file = path.resolve(import.meta.dirname, 'wwwroot/hot')
  const remove = () => fs.rmSync(file, { force: true })
  return {
    name: 'inertia-net-hot-file',
    apply: 'serve',
    configureServer(server) {
      server.httpServer?.once('listening', () => {
        fs.mkdirSync(path.dirname(file), { recursive: true })
        fs.writeFileSync(file, server.resolvedUrls?.local[0] ?? 'http://localhost:5174')
      })
      server.httpServer?.once('close', remove)
      for (const signal of ['SIGINT', 'SIGTERM'] as const) {
        process.once(signal, () => (remove(), process.exit()))
      }
      process.once('exit', remove)
    },
  }
}

export default defineConfig({
  base: '/build/',
  publicDir: false,
  plugins: [vue(), inertia(), hotFile()],
  server: { port: 5174, strictPort: true },
  build: {
    outDir: 'wwwroot/build',
    emptyOutDir: true,
    manifest: true,
    rolldownOptions: { input: 'ClientApp/app.ts' },
  },
})
