// Nitro's cached functions write under cache:. Mounted at startup so REDIS_URL is read at run time, not baked in at build; `lib` bundles ioredis into the Nitro output.
import { definePlugin } from 'nitro'
import { useStorage } from 'nitro/storage'
import redisDriver from 'unstorage/drivers/redis'

export default definePlugin(() => {
  useStorage().mount('cache', redisDriver({ url: process.env.REDIS_URL, base: 'bench', lib: () => import('ioredis') }))
})
