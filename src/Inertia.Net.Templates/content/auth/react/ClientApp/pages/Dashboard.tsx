import Layout from '../Layout'
import type { InertiaPages } from '../types/inertia'

export default function Dashboard({ name }: InertiaPages['Dashboard']) {
  return (
    <Layout>
      <h1>Dashboard</h1>
      <p>You are signed in as {name}.</p>
    </Layout>
  )
}
