// The "-cluster" builds of the Node apps (run.sh mounts this file and runs `node /cluster.mjs <server entry>`): one server process per
// CPU (WEB_CONCURRENCY), sharing the listening port through node:cluster, as PM2's cluster mode does.
import cluster from 'node:cluster'
import net from 'node:net'
import { availableParallelism } from 'node:os'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'

if (cluster.isPrimary) {
  const workers = Number(process.env.WEB_CONCURRENCY) || availableParallelism()
  for (let i = 0; i < workers; i++) cluster.fork()
  // A worker that dies stops the app, so a broken cluster fails the run instead of crash-looping beside the live workers.
  cluster.on('exit', (worker, code, signal) => {
    console.error(`worker ${worker.process.pid} exited (${signal ?? code})`)
    process.exit(1)
  })
} else {
  // Nitro 3 (TanStack Start) listens with exclusive: true, which keeps workers from sharing the port; its own node-cluster preset
  // passes exclusive: false, as this does for every app.
  const listen = net.Server.prototype.listen
  net.Server.prototype.listen = function (...args) {
    if (args[0] && typeof args[0] === 'object') args[0] = { ...args[0], exclusive: false }
    return listen.apply(this, args)
  }
  await import(pathToFileURL(resolve(process.argv[2])).href)
}
