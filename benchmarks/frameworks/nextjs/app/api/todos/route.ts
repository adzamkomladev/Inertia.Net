import { createTodo, todos } from '../../../shared/server'

export async function GET(request: Request) {
  return Response.json(await todos(new URL(request.url).searchParams))
}

// A route handler rather than a server action: the form posts JSON to a stable URL, which a load test can call directly.
export async function POST(request: Request) {
  return createTodo(await request.json().catch(() => null))
}
