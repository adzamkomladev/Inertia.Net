import { createInertiaApp } from '@inertiajs/vue3'
// Live props over SignalR: uncomment, then call AddInertiaLive().AddSignalR() and MapInertiaLiveHub() in Program.cs.
// import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and the Vue app setup.
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
  // live: signalr(),
})
