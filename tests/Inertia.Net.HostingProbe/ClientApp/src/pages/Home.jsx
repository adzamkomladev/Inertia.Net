// The anonymous landing page: the same JSON for every visitor, so the SSR render cache can serve it.
import Layout from '../Layout'

export default function Home({ appName, features }) {
  return (
    <Layout appName={appName}>
      <section className="hero">
        <h1>Plan the week in one list</h1>
        <p>Todos, projects and reports for small teams.</p>
        <a href="/register" className="button">Start free</a>
      </section>
      <section className="features">
        {features.map((feature) => (
          <article key={feature.title}>
            <h2>{feature.title}</h2>
            <p>{feature.body}</p>
            <a href="/features">Learn more</a>
          </article>
        ))}
      </section>
    </Layout>
  )
}
