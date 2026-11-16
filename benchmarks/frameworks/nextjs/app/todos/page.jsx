import { connection } from 'next/server'
import TodosPage from '../../shared/TodosPage.jsx'
import { todos } from '../../shared/server.js'
import Spa from '../spa.jsx'

export default async function Page({ searchParams }) {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="todos" />
  // Rendered on every request, like the other apps (without it Next.js would prerender the pages that read no request data).
  await connection()
  return <TodosPage {...await todos(new URLSearchParams(await searchParams))} />
}
