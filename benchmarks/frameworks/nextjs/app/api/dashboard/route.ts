import { connection } from 'next/server'
import { dashboard } from '../../../lib/dashboard'
import { cachedStats, tuned } from '../../../lib/tuned'

export async function GET() {
  await connection()
  return Response.json(await (tuned ? cachedStats() : dashboard()))
}
