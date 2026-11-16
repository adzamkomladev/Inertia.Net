import { connection } from 'next/server'
import { computeOnPool } from '../../../../shared/server'

export async function GET(request: Request) {
  await connection()
  return Response.json(await computeOnPool(new URL(request.url).searchParams))
}
