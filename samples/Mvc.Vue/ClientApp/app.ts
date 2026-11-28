import { createInertiaApp } from '@inertiajs/vue3'
import { live } from './lib/live'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**), the Vue app setup, and hydrates when the page was server-rendered.
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
  live,
})
