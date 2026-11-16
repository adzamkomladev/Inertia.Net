import { connection } from 'next/server'
import ComputePage from '../../shared/ComputePage.jsx'
import { computeSequential } from '../../shared/server.js'
import Spa from '../spa.jsx'

export default async function Page({ searchParams }) {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="compute" />
  // Rendered on every request, like the other apps (without it Next.js would prerender the pages that read no request data).
  await connection()
  return <ComputePage {...await computeSequential(new URLSearchParams(await searchParams))} />
}
