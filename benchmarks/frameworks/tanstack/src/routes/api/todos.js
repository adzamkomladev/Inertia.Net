import { createFileRoute } from '@tanstack/react-router'
import { todos } from '../../shared/server.js'

// The page's props as JSON (a server route), for measuring the data path without rendering.
export const Route = createFileRoute('/api/todos')({
  server: { handlers: { GET: async ({ request }) => Response.json(await todos(new URL(request.url).searchParams)) } },
})
