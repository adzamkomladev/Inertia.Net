// SignalR transport for Inertia.Net live props (Inertia.Net.Live.SignalR on the server).
// Enable it in app.tsx/app.ts with createInertiaApp({ live: signalr() }). @microsoft/signalr is loaded on the first subscription,
// so pages without live props, and server-side rendering, never load it.
import type { LiveChannel, LiveChannelTransport, LiveOptions } from '@inertiajs/core'
import type { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'

export type SignalROptions = Omit<LiveOptions, 'transport'> & {
  /** The hub path given to app.MapInertiaLiveHub(). */
  url?: string
  /** Stops the connection this many milliseconds after the last channel is left. Unset keeps it open. */
  idleTimeout?: number
  /** Adjusts the connection before it is built, e.g. to set transports or logging. */
  configure?: (builder: HubConnectionBuilder) => HubConnectionBuilder
  /** Called once the connection is created. */
  onConnection?: (connection: HubConnection) => void
}

type Subscription = { channel: LiveChannel; handler: (payload: unknown) => void }

const channelKey = (channel: LiveChannel) => `${channel.type}:${channel.name}`

const warn = (key: string, reason?: unknown) =>
  console.warn(`Inertia could not subscribe to the live channel "${key}".`, ...(reason === undefined ? [] : [reason]))

export function signalr({ url = '/inertia-live', idleTimeout, configure, onConnection, ...options }: SignalROptions = {}): LiveOptions {
  const subscriptions = new Map<string, Subscription>()
  let connection: HubConnection | null = null
  let starting: Promise<void> | null = null
  let status: ((connected: boolean) => void) | null = null
  let restartAttempt = 0
  let restartTimer: ReturnType<typeof setTimeout> | undefined
  let idleTimer: ReturnType<typeof setTimeout> | undefined

  const isConnected = () => connection?.state === 'Connected'

  // Never throws: a refused or failed subscription is a warning, as with the built-in transports.
  const join = async (key: string, { channel }: Subscription) => {
    try {
      if (!(await connection!.invoke<boolean>('Subscribe', channel.type, channel.name))) warn(key)
    } catch (error) {
      warn(key, error)
    }
  }

  // Group memberships belong to a connection id, so every reconnect has to join every channel again.
  const joinAll = () => Promise.all([...subscriptions].map(([key, subscription]) => join(key, subscription)))

  const scheduleRestart = () => {
    clearTimeout(restartTimer)
    if (subscriptions.size === 0) return
    const delay = Math.min(30_000, 1_000 * 2 ** restartAttempt++)
    restartTimer = setTimeout(() => {
      if (subscriptions.size > 0) void start()
    }, delay)
  }

  const start = (): Promise<void> =>
    (starting ??= (async () => {
      if (!connection) {
        const signalR = await import('@microsoft/signalr')
        const builder = new signalR.HubConnectionBuilder()
          .withUrl(url)
          .withAutomaticReconnect()
          .configureLogging(signalR.LogLevel.Warning)
        connection = (configure ? configure(builder) : builder).build()
        connection.on('inertia', (key: string, payload: unknown) => subscriptions.get(key)?.handler(payload))
        connection.onreconnecting(() => status?.(false))
        connection.onreconnected(async () => {
          await joinAll()
          status?.(true)
        })
        connection.onclose(() => {
          // Automatic reconnects gave up, or the connection was stopped: start again while any channel is in use.
          starting = null
          status?.(false)
          scheduleRestart()
        })
        onConnection?.(connection)
      }

      await connection.start()
      restartAttempt = 0
      await joinAll()
      // Reported after the first start too: Inertia refreshes live props on a reconnect only after it has seen one connect.
      status?.(true)
    })().catch((error) => {
      starting = null
      console.warn('Inertia could not connect to the live hub.', error)
      scheduleRestart()
    }))

  const transport: LiveChannelTransport = {
    subscribeToChannel(channel, handler) {
      const key = channelKey(channel)
      const subscription = { channel, handler }
      const isNew = !subscriptions.has(key)
      subscriptions.set(key, subscription)
      clearTimeout(idleTimer)

      if (isConnected()) {
        if (isNew) void join(key, subscription)
      } else {
        void start()
      }

      return () => {
        // A newer subscription to the same channel replaced this one (Inertia subscribes before it unsubscribes).
        if (subscriptions.get(key) !== subscription) return
        subscriptions.delete(key)
        if (isConnected()) void connection!.invoke('Unsubscribe', channel.type, channel.name).catch(() => {})
        if (subscriptions.size === 0 && idleTimeout !== undefined) {
          idleTimer = setTimeout(() => {
            if (subscriptions.size === 0) void connection?.stop()
          }, idleTimeout)
        }
      }
    },
    // Read at call time: the connection id changes on every reconnect.
    socketId: () => connection?.connectionId ?? null,
    onStatusChange(callback) {
      status = callback
      return () => {
        if (status === callback) status = null
      }
    },
  }

  return { transport, ...options }
}
