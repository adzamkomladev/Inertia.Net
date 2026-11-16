import { createFileRoute } from '@tanstack/react-router'
import { computeSequential } from '../../../shared/server.js'

// The page's props as JSON (a server route), for measuring the data path without rendering.
export const Route = createFileRoute('/api/compute/')({
  server: { handlers: { GET: async ({ request }) => Response.json(await computeSequential(new URL(request.url).searchParams)) } },
})
