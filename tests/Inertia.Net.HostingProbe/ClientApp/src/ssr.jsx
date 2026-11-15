// SSR entry: Inertia's render server on the port SsrNodeProcess passes in INERTIA_SSR_PORT (the probe sets a free one per run),
// bound to loopback. Runs under Node or Bun.
import { createInertiaApp } from '@inertiajs/react'
import createServer from '@inertiajs/react/server'
import { renderToString } from 'react-dom/server'

const pages = import.meta.glob('./pages/**/*.jsx', { eager: true })
const render = createInertiaApp({ resolve: (name) => pages[`./pages/${name}.jsx`] })

createServer(async (page) => (await render)(page, renderToString), {
  port: Number(process.env.INERTIA_SSR_PORT ?? 13714),
  host: '127.0.0.1',
})
