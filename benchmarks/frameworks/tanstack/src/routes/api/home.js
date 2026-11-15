import { createFileRoute } from '@tanstack/react-router'
import { home } from '../../shared/server.js'

// The page's props as JSON (a server route), for measuring the data path without rendering.
export const Route = createFileRoute('/api/home')({
  server: { handlers: { GET: async ({ request }) => Response.json(await home(new URL(request.url).searchParams)) } },
})
