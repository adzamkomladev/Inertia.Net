import { Link } from '@inertiajs/react'
import Layout from '../Layout'
import type { InertiaPages } from '../types/inertia'

export default function Home({ message }: InertiaPages['Home']) {
  return (
    <Layout>
      <h1>Home</h1>
      <p>{message}</p>
      <Link href="/todos">See the todos</Link>
    </Layout>
  )
}
