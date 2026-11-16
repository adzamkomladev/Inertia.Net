// The hosting probe's React pages (PROBE_MODE=ssr): the client build goes to ../wwwroot/build (with a manifest for @vite), the SSR
// bundle to ../ssr/ssr.mjs. The SSR bundle inlines every dependency, so it runs from the publish directory without node_modules.
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig(({ isSsrBuild }) => ({
  base: '/build/',
  publicDir: false,
  plugins: [react()],
  ssr: { noExternal: true },
  // Production React in the SSR bundle: inlined dependencies are not replaced at runtime, so their dev builds would ship too.
  define: isSsrBuild ? { 'process.env.NODE_ENV': JSON.stringify('production') } : {},
  build: isSsrBuild
    ? { outDir: '../ssr', emptyOutDir: true, rolldownOptions: { output: { entryFileNames: 'ssr.mjs' } } }
    : { outDir: '../wwwroot/build', emptyOutDir: true, manifest: 'manifest.json', rolldownOptions: { input: 'src/app.jsx' } },
}))
