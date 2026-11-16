import { connection } from 'next/server'
import TodosPage from '../../shared/TodosPage'
import { todos } from '../../shared/server'
import Spa from '../spa'

// Cache Components: block on the data, so the response is the whole page (as in the other apps), not a streamed shell.
export const instant = false

export default async function Page({ searchParams }: PageProps<'/todos'>) {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="todos" />
  // Rendered on every request, like the other apps (without it Next.js would prerender the pages that read no request data).
  await connection()
  return <TodosPage {...await todos(new URLSearchParams((await searchParams) as Record<string, string>))} />
}
