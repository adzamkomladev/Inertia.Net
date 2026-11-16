import { connection } from 'next/server'
import Spa from '../../spa'
import NewTodoForm from './form'

// Cache Components: block on the data, so the response is the whole page (as in the other apps), not a streamed shell.
export const instant = false

export default async function Page() {
  if (process.env.BENCH_MODE === 'spa') return <Spa page="new" />
  // Rendered on every request, like the other pages.
  await connection()
  return <NewTodoForm />
}
