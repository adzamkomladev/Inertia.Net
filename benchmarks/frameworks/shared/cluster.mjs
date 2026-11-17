// The "-cluster" builds of the Node apps (run.sh mounts this file and runs `node /cluster.mjs <server entry>`): one server process per
// CPU (WEB_CONCURRENCY), sharing the listening port through node:cluster, as PM2's cluster mode does. A crashed worker is replaced.
import cluster from 'node:cluster'
import { availableParallelism } from 'node:os'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'

if (cluster.isPrimary) {
  const workers = Number(process.env.WEB_CONCURRENCY) || availableParallelism()
  for (let i = 0; i < workers; i++) cluster.fork()
  cluster.on('exit', (worker, code, signal) => signal !== 'SIGTERM' && cluster.fork())
  process.on('SIGTERM', () => process.exit(0))
} else {
  await import(pathToFileURL(resolve(process.argv[2])).href)
}
