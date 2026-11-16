// The dashboard's 60 s cache in Redis, where every app of the benchmark caches. TanStack Start has no server-side data cache of its
// own (its router cache lives in the browser), so this is the plain cache-aside a team would write with node-redis.
import { createClient } from 'redis'
import { dashboardStats } from './shared/server'

declare global {
  var __benchRedis: ReturnType<typeof connect> | undefined
}

const connect = () => createClient({ url: process.env.REDIS_URL }).connect()

async function cached<T>(key: string, seconds: number, load: () => Promise<T>): Promise<T> {
  const redis = await (globalThis.__benchRedis ??= connect())
  const hit = await redis.get(key)
  if (hit) return JSON.parse(hit) as T
  const value = await load()
  await redis.set(key, JSON.stringify(value), { expiration: { type: 'EX', value: seconds } })
  return value
}

export const dashboard = async () => ({ stats: await cached('bench:dashboard', 60, dashboardStats) })
