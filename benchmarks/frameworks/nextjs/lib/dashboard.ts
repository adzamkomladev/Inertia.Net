import { cacheLife } from 'next/cache'
import { dashboardStats } from '../shared/server'

// Cached for 60 s in Redis (the remote cache handler), like the dashboard of every other app.
export async function dashboard() {
  'use cache: remote'
  cacheLife({ stale: 60, revalidate: 60, expire: 60 })
  return { stats: await dashboardStats() }
}
