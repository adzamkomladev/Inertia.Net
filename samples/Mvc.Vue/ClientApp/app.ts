import { createInertiaApp } from '@inertiajs/vue3'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and the Vue app setup.
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
})
