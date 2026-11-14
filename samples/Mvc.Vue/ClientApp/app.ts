import { createInertiaApp } from '@inertiajs/vue3'
import { withInertiaModal } from '@inertiaui/modal-vue'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and the Vue app setup. withInertiaModal adds InertiaUI Modal's
// stack to the app (Users/Edit opens in one).
createInertiaApp({
  serverHead: true, // head tags set on the server with Render(...).WithHead(...)
  withApp: (app) => withInertiaModal(app as unknown as Parameters<typeof withInertiaModal>[0]),
})
