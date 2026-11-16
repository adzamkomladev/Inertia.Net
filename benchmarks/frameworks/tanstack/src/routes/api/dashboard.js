import { createFileRoute } from '@tanstack/react-router'
import { dashboard } from '../../shared/server.js'

// The page's props as JSON (a server route), for measuring the data path without rendering.
export const Route = createFileRoute('/api/dashboard')({
  server: { handlers: { GET: async ({ request }) => Response.json(await dashboard(new URL(request.url).searchParams)) } },
})
