// The CPU-heavy work behind /compute, for the Node apps. Same algorithm as Compute.cs (Inertia.Net) and Compute.php (Laravel):
// 16 batches of n/16 records from a xorshift32 generator, each sorted by (value, id), grouped into 64 buckets and hashed with SHA-256;
// the checksum is the SHA-256 of the 16 batch digests. /compute runs the batches one after another, /compute/parallel on a worker_threads pool.
import { createHash } from 'node:crypto'
import { availableParallelism } from 'node:os'
import { Worker } from 'node:worker_threads'

export const BATCHES = 16

// Self-contained (only its arguments and globals): the worker threads run it from its source text.
export function batch(b, m, createHash) {
  let s = Math.imul(b + 1, 2654435761) >>> 0
  const next = () => {
    s = (s ^ (s << 13)) >>> 0
    s = (s ^ (s >>> 17)) >>> 0
    s = (s ^ (s << 5)) >>> 0
    return s
  }
  const records = new Array(m)
  for (let i = 0; i < m; i++) {
    const value = next() % 1000000
    records[i] = { id: b * m + i, value, group: next() % 64 }
  }
  records.sort((x, y) => x.value - y.value || x.id - y.id)
  const counts = new Array(64).fill(0)
  const sums = new Array(64).fill(0)
  for (const r of records) {
    counts[r.group]++
    sums[r.group] += r.value
  }
  const ids = records.map((r) => r.id).join(',')
  const groups = counts.map((c, g) => g + ':' + c + ':' + sums[g]).join(';')
  return createHash('sha256').update(ids + '|' + groups).digest('hex')
}

const checksum = (digests) => createHash('sha256').update(digests.join('')).digest('hex')

export function compute(n) {
  const m = Math.floor(n / BATCHES)
  return checksum(Array.from({ length: BATCHES }, (_, b) => batch(b, m, createHash)))
}

const workerSource = `const { parentPort } = require('node:worker_threads')
const { createHash } = require('node:crypto')
const batch = ${batch.toString()}
parentPort.on('message', ({ b, m }) => parentPort.postMessage(batch(b, m, createHash)))`

// A fixed pool of worker threads, one batch per message. BENCH_WORKERS sets its size (run.sh passes the container's CPU limit:
// Node sizes availableParallelism() from the host's CPUs, not the container's quota).
function createPool(size) {
  const idle = []
  const queue = []
  const pump = () => {
    while (idle.length > 0 && queue.length > 0) {
      const worker = idle.pop()
      worker.task = queue.shift()
      worker.postMessage(worker.task.message)
    }
  }
  for (let i = 0; i < size; i++) {
    const worker = new Worker(workerSource, { eval: true })
    worker.unref()
    worker.on('message', (digest) => {
      const task = worker.task
      worker.task = null
      idle.push(worker)
      task.resolve(digest)
      pump()
    })
    worker.on('error', (error) => worker.task?.reject(error))
    idle.push(worker)
  }
  return (message) => new Promise((resolve, reject) => (queue.push({ message, resolve, reject }), pump()))
}

export async function computeParallel(n) {
  // On globalThis: frameworks may bundle this module more than once (pages and API routes), and one pool per process is the point.
  const run = (globalThis.__benchPool ??= createPool(Number(process.env.BENCH_WORKERS) || availableParallelism()))
  const m = Math.floor(n / BATCHES)
  return checksum(await Promise.all(Array.from({ length: BATCHES }, (_, b) => run({ b, m }))))
}
