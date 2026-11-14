import { createInertiaApp } from '@inertiajs/react'
import { createRoot, hydrateRoot } from 'react-dom/client'
import { live } from './lib/live'
import { withModals } from './lib/modal'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). withModals adds InertiaUI Modal's stack (Users/Edit opens in one).
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
  live,
  setup({ el, App, props }) {
    const app = withModals(App, props)
    if (el!.hasAttribute('data-server-rendered')) hydrateRoot(el!, app)
    else createRoot(el!).render(app)
  },
})
