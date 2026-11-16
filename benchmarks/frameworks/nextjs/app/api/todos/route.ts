import { createTodo, todos } from '../../../shared/server'
import { cachedTodos, tuned } from '../../../lib/tuned'

export async function GET(request: Request) {
  const params = new URL(request.url).searchParams
  return Response.json(await (tuned ? cachedTodos(params) : todos(params)))
}

// A route handler rather than a server action: the form posts JSON to a stable URL, which a load test can call directly.
export async function POST(request: Request) {
  return createTodo(await request.json().catch(() => null))
}
