import { createInertiaApp } from '@inertiajs/svelte'
import { live } from './lib/live'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and hydrates when the page was server-rendered.
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
  live,
})
