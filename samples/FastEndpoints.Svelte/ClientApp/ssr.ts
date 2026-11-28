import { createInertiaApp } from '@inertiajs/svelte'

// @inertiajs/vite wraps this call with the SSR server bootstrap (port 13714) in `vite build --ssr`.
createInertiaApp({ serverHead: true }) // head tags set on the server with Render(...).WithHead(...)
