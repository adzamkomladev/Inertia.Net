// Nitro's cached functions write under cache:. Mounted at startup so REDIS_URL is read at run time, not baked in at build.
import redisDriver from 'unstorage/drivers/redis'

export default defineNitroPlugin(() => {
  useStorage().mount('cache', redisDriver({ url: process.env.REDIS_URL, base: 'bench' }))
})
