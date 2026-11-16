import { createFileRoute } from '@tanstack/react-router'
import { createTodo, todos } from '../../shared/server'

// GET: the page's props as JSON. POST: the new-todo form, as a server route rather than a server function, so the form posts JSON
// to a stable URL that a load test can call directly.
export const Route = createFileRoute('/api/todos')({
  server: {
    handlers: {
      GET: async ({ request }) => Response.json(await todos(new URL(request.url).searchParams)),
      POST: async ({ request }) => createTodo(await request.json().catch(() => null)),
    },
  },
})
