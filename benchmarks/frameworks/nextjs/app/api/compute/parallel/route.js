import { computeOnPool } from '../../../../shared/server.js'

export async function GET(request) {
  return Response.json(await computeOnPool(new URL(request.url).searchParams))
}
