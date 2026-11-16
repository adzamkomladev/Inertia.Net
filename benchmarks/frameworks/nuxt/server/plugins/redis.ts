// Nitro's cached functions write under cache:. Mounted at startup so REDIS_URL is read at run time, not baked in at build.
import redisDriver from 'unstorage/drivers/redis'

export default defineNitroPlugin(() => {
  if (process.env.BENCH_TUNED === '1') return // tuned keeps Nitro's default in-memory cache, like Inertia.Net tuned
  useStorage().mount('cache', redisDriver({ url: process.env.REDIS_URL, base: 'bench' }))
})
