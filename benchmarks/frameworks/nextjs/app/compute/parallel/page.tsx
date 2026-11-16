import { connection } from 'next/server'
import ComputePage from '../../../shared/ComputePage'
import { computeOnPool } from '../../../shared/server'
import Spa from '../../spa'

// Cache Components: block on the data, so the response is the whole page (as in the other apps), not a streamed shell.
export const instant = false

export default async function Page({ searchParams }: PageProps<'/compute/parallel'>) {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="compute" />
  // Rendered on every request, like the other apps (without it Next.js would prerender the pages that read no request data).
  await connection()
  return <ComputePage {...await computeOnPool(new URLSearchParams((await searchParams) as Record<string, string>))} />
}
