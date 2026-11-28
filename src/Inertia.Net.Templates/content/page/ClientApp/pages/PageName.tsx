import Layout from 'PARENTS/Layout'
import type { InertiaPages } from 'PARENTS/types/inertia'

export default function Page({ message }: InertiaPages['PageName']) {
  return (
    <Layout>
      <h1>PageName</h1>
      <p>{message}</p>
    </Layout>
  )
}
