import { cacheLife } from 'next/cache'
import DashboardPage from '../shared/DashboardPage'
import TodosPage from '../shared/TodosPage'
import { todosQuery } from '../shared/data'
import { dashboardStats, todos } from '../shared/server'

// BENCH_TUNED=1 (fixed at build time): Next.js's own caching, set like Inertia.Net tuned. 'use cache' keeps its entries in memory and
// caches the rendered component, not just the data: todo pages for 10 s and the dashboard for 60 s, each served stale while it refreshes.
export const tuned = process.env.BENCH_TUNED === '1'

export async function cachedTodos(params: URLSearchParams) {
  const { page, perPage } = todosQuery(params) // one cache entry per clamped page, not per raw query string
  return todosFor(page, perPage)
}

async function todosFor(page: number, perPage: number) {
  'use cache'
  cacheLife({ stale: 10, revalidate: 10, expire: 70 })
  return todos(new URLSearchParams({ page: String(page), perPage: String(perPage) }))
}

export async function cachedStats() {
  'use cache'
  cacheLife({ stale: 60, revalidate: 60, expire: 360 })
  return { stats: await dashboardStats() }
}

export async function CachedTodosPage({ page, perPage }: { page: number; perPage: number }) {
  'use cache'
  cacheLife({ stale: 10, revalidate: 10, expire: 70 })
  return <TodosPage {...await todosFor(page, perPage)} />
}

export async function CachedDashboardPage() {
  'use cache'
  cacheLife({ stale: 60, revalidate: 60, expire: 360 })
  return <DashboardPage {...await cachedStats()} />
}
