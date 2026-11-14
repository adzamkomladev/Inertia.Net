import { createInertiaApp } from '@inertiajs/svelte'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and mounts the app.
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
})
