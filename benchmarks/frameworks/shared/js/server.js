// Data for every route in the Node apps (Next.js, TanStack Start): the server-rendered pages and the JSON routes call the same functions.
import pg from 'pg'
import { createClient } from 'redis'
import { compute, computeParallel } from './compute.js'
import { computeN, features, todosQuery } from './data.js'

// On globalThis: Next.js may bundle this module into several route bundles, and one pool per process is what the other apps have.
const db = (globalThis.__benchDb ??= new pg.Pool({ connectionString: process.env.DATABASE_URL, max: 10 }))
const redis = () => (globalThis.__benchRedis ??= createClient({ url: process.env.REDIS_URL }).connect())

export const home = () => ({ features })

export async function todos(params) {
  const { perPage, page } = todosQuery(params)
  const { rows } = await db.query(
    "SELECT id, title, done, priority, assignee, to_char(due_at, 'YYYY-MM-DD') AS due FROM todos ORDER BY created_at DESC, id DESC LIMIT $1 OFFSET $2",
    [perPage, (page - 1) * perPage],
  )
  return { todos: rows, page, perPage }
}

export async function dashboard() {
  const cache = await redis()
  const cached = await cache.get('bench:dashboard')
  if (cached) return { stats: JSON.parse(cached) }
  const { rows } = await db.query(
    'SELECT assignee, count(*)::int AS total, (count(*) FILTER (WHERE done))::int AS done, sum(priority)::int AS priority_sum FROM todos GROUP BY assignee ORDER BY assignee',
  )
  const byAssignee = rows.map((r) => ({ assignee: r.assignee, total: r.total, done: r.done, prioritySum: r.priority_sum }))
  const stats = { total: byAssignee.reduce((s, r) => s + r.total, 0), done: byAssignee.reduce((s, r) => s + r.done, 0), byAssignee }
  await cache.set('bench:dashboard', JSON.stringify(stats), { expiration: { type: 'EX', value: 60 } })
  return { stats }
}

export function computeSequential(params) {
  const n = computeN(params)
  return { n, checksum: compute(n), parallel: false }
}

export async function computeOnPool(params) {
  const n = computeN(params)
  return { n, checksum: await computeParallel(n), parallel: true }
}
