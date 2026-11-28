// npm run build: type-check the Svelte components, then build the client bundle (+ SSR bundle when INERTIA_SSR=1).
import { spawnSync } from 'node:child_process'

const run = (cmd) => {
  const { status } = spawnSync(cmd, { stdio: 'inherit', shell: true })
  if (status) process.exit(status)
}

run('svelte-check --tsconfig ./tsconfig.json')
run('vite build')
if (process.env.INERTIA_SSR === '1') run('vite build --ssr')
