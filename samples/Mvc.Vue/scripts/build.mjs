// npm run build: type-check, then the client bundle.
import { spawnSync } from 'node:child_process'

const run = (cmd) => {
  const { status } = spawnSync(cmd, { stdio: 'inherit', shell: true })
  if (status) process.exit(status)
}

run('vue-tsc --noEmit')
run('vite build')
