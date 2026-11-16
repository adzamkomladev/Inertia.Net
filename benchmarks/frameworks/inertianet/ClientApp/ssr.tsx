import { createInertiaApp } from '@inertiajs/react'
import createServer from '@inertiajs/react/server'
import { renderToString } from 'react-dom/server'

declare const process: { env: Record<string, string | undefined> }

const app = createInertiaApp()

// Our own createServer call so the port is read at run time: Inertia.Net gives each Node instance its own INERTIA_SSR_PORT.
createServer(async (page) => {
  const render = await app
  if (!render) throw new Error('createInertiaApp returned no SSR renderer')
  return render(page, renderToString)
}, {
  host: '127.0.0.1',
  port: Number(process.env.INERTIA_SSR_PORT ?? 13714),
})
