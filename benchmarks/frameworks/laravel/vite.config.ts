import inertia from '@inertiajs/vite'
import react from '@vitejs/plugin-react'
import laravel from 'laravel-vite-plugin'
import { defineConfig } from 'vite'

export default defineConfig(({ isSsrBuild }) => ({
  plugins: [laravel({ input: 'resources/js/app.tsx', refresh: true }), inertia(), react()],
  // As in the Inertia.Net app: the SSR bundle inlines its dependencies and uses production React, so it runs without node_modules.
  ssr: { noExternal: true },
  define: isSsrBuild ? { 'process.env.NODE_ENV': JSON.stringify('production') } : {},
  server: { host: '0.0.0.0', port: 5173, strictPort: true, hmr: { host: 'localhost' } },
}))
