import { connection } from 'next/server'
import { home } from '../../../shared/server'

export async function GET() {
  await connection()
  return Response.json(await home())
}
