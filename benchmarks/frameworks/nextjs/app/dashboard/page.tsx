import { connection } from 'next/server'
import DashboardPage from '../../shared/DashboardPage'
import { dashboard } from '../../lib/dashboard'
import Spa from '../spa'
import { CachedDashboardPage, tuned } from '../../lib/tuned'

// Cache Components: block on the data, so the response is the whole page (as in the other apps), not a streamed shell.
export const instant = false

export default async function Page() {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="dashboard" />
  // Rendered on every request, like the other apps (without it Next.js would prerender the pages that read no request data).
  await connection()
  if (tuned) return <CachedDashboardPage />
  return <DashboardPage {...await dashboard()} />
}
