import { connection } from 'next/server'
import { computeSequential } from '../../../shared/server'

export async function GET(request: Request) {
  await connection()
  return Response.json(await computeSequential(new URL(request.url).searchParams))
}
