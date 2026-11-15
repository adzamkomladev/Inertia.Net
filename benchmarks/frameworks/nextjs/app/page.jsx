import { connection } from 'next/server'
import HomePage from '../shared/HomePage.jsx'
import { home } from '../shared/server.js'
import Spa from './spa.jsx'

export default async function Page({ searchParams }) {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="home" />
  // Rendered on every request, like the other apps (without it Next.js would prerender the pages that read no request data).
  await connection()
  return <HomePage {...await home(new URLSearchParams(await searchParams))} />
}
