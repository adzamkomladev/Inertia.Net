import Layout from './Layout'
import type { Feature } from './types'

export default function HomePage({ features }: { features: Feature[] }) {
  return (
    <Layout title="Home" page="home">
      <ul>
        {features.map((f) => (
          <li key={f.title}>
            <strong>{f.title}</strong> {f.body}
          </li>
        ))}
      </ul>
    </Layout>
  )
}
