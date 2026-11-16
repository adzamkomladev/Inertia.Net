import { computeSequential } from '../../../shared/server.js'

export async function GET(request) {
  return Response.json(await computeSequential(new URL(request.url).searchParams))
}
