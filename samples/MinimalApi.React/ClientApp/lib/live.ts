import { signalr } from './inertia-signalr'
import { sse } from './inertia-sse'

// Live props over the SignalR hub mapped by app.MapInertiaLiveHub(); @microsoft/signalr loads on the first subscription.
// A build with VITE_LIVE_TRANSPORT=sse uses the Server-Sent Events endpoint of app.MapInertiaLiveSse() instead (LIVE_TRANSPORT=sse).
// Shared by createInertiaApp({ live }) and the pages that call liveReload(live, ...).
// The E2E suite sets window.__e2e before the app starts, to reach the connection (e.g. to drop it and test reconnects).
const expose = (connection: unknown) => {
  if ((window as any).__e2e) (window as any).__inertiaLive = connection
}

export const live = import.meta.env.VITE_LIVE_TRANSPORT === 'sse' ? sse({ onConnection: expose }) : signalr({ onConnection: expose })
