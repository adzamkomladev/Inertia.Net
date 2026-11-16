import Layout from './Layout.jsx'

export default function HomePage({ features }) {
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
