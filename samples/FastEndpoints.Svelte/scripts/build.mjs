// npm run build: type-check the Svelte components, then build the client bundle.
import { spawnSync } from 'node:child_process'

const run = (cmd) => {
  const { status } = spawnSync(cmd, { stdio: 'inherit', shell: true })
  if (status) process.exit(status)
}

run('svelte-check --tsconfig ./tsconfig.json')
run('vite build')
