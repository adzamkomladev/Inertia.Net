import { signalr } from './inertia-signalr'

// Live props over the SignalR hub mapped by app.MapInertiaLiveHub(); @microsoft/signalr loads on the first subscription.
// Shared by createInertiaApp({ live }) and the pages that call liveReload(live, ...).
export const live = signalr({
  // The E2E suite sets window.__e2e before the app starts, to reach the connection (e.g. to drop it and test reconnects).
  onConnection: (connection) => {
    if ((window as any).__e2e) (window as any).__inertiaLive = connection
  },
})
