import { dashboard } from '../../../shared/server.js'

export async function GET(request) {
  return Response.json(await dashboard(new URL(request.url).searchParams))
}
