// Server-Sent Events transport for Inertia.Net live props (Inertia.Net.Live.Sse on the server).
// Enable it in app.tsx/app.ts with createInertiaApp({ live: sse() }). It uses the browser's EventSource: no client library.
// The channels are part of the stream URL, so the stream reopens (once per change, batched) when a page joins or leaves a channel.
import type { LiveChannel, LiveChannelTransport, LiveOptions } from '@inertiajs/core'

/** The open stream, for tests and diagnostics: close it with stop() to see the transport reconnect. */
export type SseConnection = { readonly state: 'Connected' | 'Connecting' | 'Disconnected'; stop(): void }

export type SseOptions = Omit<LiveOptions, 'transport'> & {
  /** The endpoint path given to app.MapInertiaLiveSse(). */
  url?: string
  /** Called once the transport opens its first stream. */
  onConnection?: (connection: SseConnection) => void
}

// Inertia and liveReload() may both listen on a channel: it is joined once, and every handler gets its events.
type Subscription = { channel: LiveChannel; handlers: Set<(payload: unknown) => void> }

const channelKey = (channel: LiveChannel) => `${channel.type}:${channel.name}`

export function sse({ url = '/inertia-live', onConnection, ...options }: SseOptions = {}): LiveOptions {
  const subscriptions = new Map<string, Subscription>()
  let source: EventSource | null = null
  let socketId: string | null = null
  let connected = false
  let status: ((connected: boolean) => void) | null = null
  let pending = false
  let restartAttempt = 0
  let restartTimer: ReturnType<typeof setTimeout> | undefined
  let reported = false

  const setConnected = (value: boolean) => {
    if (connected === value) return
    connected = value
    status?.(value)
  }

  const connection: SseConnection = {
    get state() {
      return connected ? 'Connected' : source ? 'Connecting' : 'Disconnected'
    },
    stop() {
      close()
      scheduleRestart()
    },
  }

  const close = () => {
    source?.close()
    source = null
    socketId = null
    setConnected(false)
  }

  const scheduleRestart = () => {
    clearTimeout(restartTimer)
    if (subscriptions.size === 0) return
    const delay = Math.min(30_000, 1_000 * 2 ** restartAttempt++)
    restartTimer = setTimeout(open, delay)
  }

  const open = () => {
    clearTimeout(restartTimer)
    source?.close()
    source = null
    socketId = null
    if (subscriptions.size === 0) {
      setConnected(false)
      return
    }

    const query = new URLSearchParams([...subscriptions.keys()].map((key) => ['channel', key]))
    const stream = new EventSource(`${url}?${query}`)
    source = stream
    stream.addEventListener('connected', (event) => {
      const { socketId: id, refused } = JSON.parse((event as MessageEvent<string>).data) as { socketId: string; refused: string[] }
      socketId = id
      restartAttempt = 0
      for (const key of refused) console.warn(`Inertia could not subscribe to the live channel "${key}".`)
      // Reported after the first connect too: Inertia refreshes live props on a reconnect only after it has seen one connect.
      // A stream reopened for a channel change is not a reconnect: the status stays connected.
      setConnected(true)
    })
    stream.addEventListener('inertia', (event) => {
      const { channel, payload } = JSON.parse((event as MessageEvent<string>).data) as { channel: string; payload: unknown }
      subscriptions.get(channel)?.handlers.forEach((handler) => handler(payload))
    })
    stream.onerror = () => {
      if (source !== stream) return
      socketId = null
      setConnected(false)
      // EventSource retries a dropped stream itself; a refused one (403, 400) is closed for good, so try again with a backoff.
      if (stream.readyState === EventSource.CLOSED) {
        source = null
        scheduleRestart()
      }
    }
    if (!reported) {
      reported = true
      onConnection?.(connection)
    }
  }

  // Joins and leaves in one task (Inertia subscribes before it unsubscribes on a visit) reopen the stream once.
  const reopen = () => {
    if (pending) return
    pending = true
    queueMicrotask(() => {
      pending = false
      open()
    })
  }

  const transport: LiveChannelTransport = {
    subscribeToChannel(channel, handler) {
      const key = channelKey(channel)
      const isNew = !subscriptions.has(key)
      const subscription = subscriptions.get(key) ?? { channel, handlers: new Set() }
      // A wrapper per call, so the same function subscribed twice is also removed twice.
      const listener = (payload: unknown) => handler(payload)
      subscription.handlers.add(listener)
      subscriptions.set(key, subscription)
      if (isNew) reopen()

      return () => {
        // The channel is left once its last handler goes.
        if (!subscription.handlers.delete(listener) || subscription.handlers.size > 0) return
        subscriptions.delete(key)
        reopen()
      }
    },
    // Read at call time: the socket id changes on every reconnect.
    socketId: () => socketId,
    onStatusChange(callback) {
      status = callback
      return () => {
        if (status === callback) status = null
      }
    },
  }

  return { transport, ...options }
}
