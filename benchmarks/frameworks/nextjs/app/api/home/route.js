import { home } from '../../../shared/server.js'

export async function GET(request) {
  return Response.json(await home(new URL(request.url).searchParams))
}
