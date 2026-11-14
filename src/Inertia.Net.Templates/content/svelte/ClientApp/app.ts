import { createInertiaApp } from '@inertiajs/svelte'
// Live props over SignalR: uncomment, then call AddInertiaLive().AddSignalR() and MapInertiaLiveHub() in Program.cs.
// import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and mounts the app.
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
  // live: signalr(),
})
