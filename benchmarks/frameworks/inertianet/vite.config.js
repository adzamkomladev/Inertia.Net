import inertia from '@inertiajs/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig(({ command, isSsrBuild }) => ({
  base: command === 'build' ? '/build/' : '/',
  publicDir: false,
  plugins: [react(), inertia({ ssr: { entry: 'ClientApp/ssr.jsx', host: '127.0.0.1', port: 13714, sourcemap: false } })],
  // The SSR bundle inlines its dependencies, so it runs from the publish directory without node_modules, with production React.
  ssr: { noExternal: true },
  define: isSsrBuild ? { 'process.env.NODE_ENV': JSON.stringify('production') } : {},
  server: { host: '0.0.0.0', port: 5173, strictPort: true },
  build: isSsrBuild
    ? { outDir: 'ssr', emptyOutDir: true }
    : { outDir: 'wwwroot/build', emptyOutDir: true, manifest: true, rolldownOptions: { input: 'ClientApp/app.jsx' } },
}))
