import { createInertiaApp } from '@inertiajs/react'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and hydrates when the page was server-rendered.
createInertiaApp({
  // Live props over the SignalR hub mapped by app.MapInertiaLiveHub(); @microsoft/signalr loads on the first subscription.
  live: signalr({
    // The E2E suite sets window.__e2e before the app starts, to reach the connection (e.g. to drop it and test reconnects).
    onConnection: (connection) => {
      if ((window as any).__e2e) (window as any).__inertiaLive = connection
    },
  }),
})
