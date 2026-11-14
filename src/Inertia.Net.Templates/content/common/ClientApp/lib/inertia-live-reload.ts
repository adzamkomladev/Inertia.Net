// Reloads props when a live event arrives, for props that cannot be Live() on the server: Scroll(...) and Merge(...) feeds.
// const stop = liveReload(live, liveEvents.PostCreated, { only: ['posts'], reset: ['posts'] }), with `live` the value given to
// createInertiaApp({ live }) and liveEvents from the generated routes.ts (o.TypeScript.LiveEvents(...) on the server).
import { router } from '@inertiajs/core'
import type { LiveChannel, LiveEnvelope, LiveOptions } from '@inertiajs/core'

export type LiveReloadOptions = {
  /** The props to reload. */
  only: string[]
  /** Props to replace instead of merge, e.g. a scroll prop, so the reload brings no duplicates. */
  reset?: string[]
  /** Events within this many milliseconds become one reload. Default 250. */
  debounce?: number
}

export function liveReload(
  live: LiveOptions,
  event: { name: string; channel: LiveChannel },
  { only, reset, debounce = 250 }: LiveReloadOptions,
): () => void {
  const { transport } = live
  let timer: ReturnType<typeof setTimeout> | undefined
  let visiting = 0
  let waiting = false

  const reload = () => {
    // A reload during another visit could be overwritten by it, or cancel it: wait for it to finish.
    if (visiting > 0) {
      waiting = true
      return
    }
    waiting = false
    router.reload({ only, reset, preserveErrors: true })
  }

  const handler = (payload: unknown) => {
    const envelope = (payload as LiveEnvelope | null)?.__inertia
    if (envelope?.event !== undefined && envelope.event !== event.name) return
    // This client caused the broadcast: it already has the change.
    if (envelope?.socketId && envelope.socketId === transport.socketId?.()) return
    clearTimeout(timer)
    timer = setTimeout(reload, debounce)
  }

  const stopStart = router.on('start', () => void visiting++)
  const stopFinish = router.on('finish', () => {
    visiting = Math.max(0, visiting - 1)
    if (visiting === 0 && waiting) reload()
  })
  const unsubscribe =
    'subscribeToChannel' in transport
      ? transport.subscribeToChannel(event.channel, handler)
      : transport.subscribe(event.channel, event.name, handler)

  return () => {
    clearTimeout(timer)
    stopStart()
    stopFinish()
    unsubscribe()
  }
}
