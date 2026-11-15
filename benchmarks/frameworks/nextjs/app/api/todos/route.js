import { todos } from '../../../shared/server.js'

export async function GET(request) {
  return Response.json(await todos(new URL(request.url).searchParams))
}
