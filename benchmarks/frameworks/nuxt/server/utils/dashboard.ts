// The dashboard's 60 s cache through Nitro's cached functions; server/plugins/redis.ts puts Nitro's cache storage in Redis.
import { dashboardStats } from '../../shared/server'

const cachedStats = defineCachedFunction(dashboardStats, { name: 'dashboard', getKey: () => 'stats', maxAge: 60, swr: false })

export const dashboard = async () => ({ stats: await cachedStats() })
