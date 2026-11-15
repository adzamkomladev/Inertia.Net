import { connection } from 'next/server'
import DashboardPage from '../../shared/DashboardPage.jsx'
import { dashboard } from '../../shared/server.js'
import Spa from '../spa.jsx'

export default async function Page({ searchParams }) {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="dashboard" />
  // Rendered on every request, like the other apps (without it Next.js would prerender the pages that read no request data).
  await connection()
  return <DashboardPage {...await dashboard(new URLSearchParams(await searchParams))} />
}
