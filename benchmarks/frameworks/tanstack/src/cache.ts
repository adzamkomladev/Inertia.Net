// The dashboard's 60 s cache through Nitro's cached functions; the Nitro plugin in server/redis.ts puts Nitro's storage in Redis.
import { defineCachedFunction } from 'nitro/cache'
import { dashboardStats } from './shared/server'

const cachedStats = defineCachedFunction(dashboardStats, { name: 'dashboard', getKey: () => 'stats', maxAge: 60, swr: false })

export const dashboard = async () => ({ stats: await cachedStats() })
