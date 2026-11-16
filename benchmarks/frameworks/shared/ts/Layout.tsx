import type { ReactNode } from 'react'

// Page chrome shared by every app. Plain <a> links, so the markup is identical whatever router the app uses.
export default function Layout({ title, page, children }: { title: string; page: string; children: ReactNode }) {
  return (
    <div className="shell" data-bench={page}>
      <header>
        <a href="/" className="brand">Bench</a>
        <nav>
          <a href="/">Home</a> <a href="/todos?perPage=25">Todos</a> <a href="/todos/new">New todo</a> <a href="/dashboard">Dashboard</a>{' '}
          <a href="/compute?n=20000">Compute</a>
        </nav>
      </header>
      <main>
        <h1>{title}</h1>
        {children}
      </main>
    </div>
  )
}
