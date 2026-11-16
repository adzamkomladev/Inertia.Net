// The cache handler behind 'use cache: remote', after the Redis example in the Next.js cacheHandlers docs: each entry is stored with
// its value bytes in base64 and a TTL of its expire time.
import type { CacheEntry, CacheHandler } from 'next/cache'
import { createClient } from 'redis'

type Stored = Omit<CacheEntry, 'value'> & { value: string }

let client: ReturnType<typeof connect> | undefined
const connect = () => createClient({ url: process.env.REDIS_URL }).connect()
const redis = () => (client ??= connect())

const handler: CacheHandler = {
  async get(cacheKey) {
    try {
      const stored = await (await redis()).get(cacheKey)
      if (!stored) return undefined
      const { value, ...entry }: Stored = JSON.parse(stored)
      const bytes = Buffer.from(value, 'base64')
      return { ...entry, value: new ReadableStream({ start: (c) => (c.enqueue(bytes), c.close()) }) }
    } catch (error) {
      console.error('cache get failed', error)
      return undefined
    }
  },
  async set(cacheKey, pendingEntry) {
    try {
      const { value, ...entry } = await pendingEntry
      const bytes = Buffer.from(await new Response(value).arrayBuffer())
      const stored: Stored = { ...entry, value: bytes.toString('base64') }
      await (await redis()).set(cacheKey, JSON.stringify(stored), Number.isFinite(entry.expire) ? { EX: Math.max(1, Math.ceil(entry.expire)) } : {})
    } catch (error) {
      console.error('cache set failed', error)
    }
  },
  // shortcut: no tag tracking, as the app never calls revalidateTag/revalidatePath; add the docs' Redis tag sync if it does.
  async refreshTags() {},
  async getExpiration() {
    return 0
  },
  async updateTags() {},
}

export default handler
