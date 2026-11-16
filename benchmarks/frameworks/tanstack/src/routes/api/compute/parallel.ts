import { createFileRoute } from '@tanstack/react-router'
import { computeOnPool } from '../../../shared/server'

// The page's props as JSON (a server route), for measuring the data path without rendering.
export const Route = createFileRoute('/api/compute/parallel')({
  server: { handlers: { GET: async ({ request }) => Response.json(await computeOnPool(new URL(request.url).searchParams)) } },
})
