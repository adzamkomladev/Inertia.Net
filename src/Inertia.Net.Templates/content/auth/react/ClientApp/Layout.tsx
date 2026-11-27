import { Link, router, usePage } from '@inertiajs/react'
import type { ReactNode } from 'react'
import { routes } from './types/routes'

export default function Layout({ children }: { children: ReactNode }) {
  // props.appName, props.auth and flash are typed by types/inertia.d.ts.
  const { props, flash } = usePage()
  const user = props.auth.user
  return (
    <div style={{ fontFamily: 'system-ui, sans-serif', maxWidth: 640, margin: '0 auto', padding: 16 }}>
      <header>
        <strong>{props.appName}</strong>
        <nav style={{ display: 'flex', gap: 12, margin: '8px 0' }}>
          <Link href={routes.home()}>Home</Link>
          <Link href={routes.todos.index()}>Todos</Link>
          {user ? (
            <>
              <Link href={routes.dashboard()}>Dashboard</Link>
              <span style={{ marginLeft: 'auto' }}>{user.name}</span>
              <button onClick={() => router.visit(routes.logout())}>Log out</button>
            </>
          ) : (
            <Link href={routes.login()} style={{ marginLeft: 'auto' }}>
              Log in
            </Link>
          )}
        </nav>
        {flash.success && <p style={{ color: 'green' }}>{flash.success}</p>}
        {flash.error && (
          <p role="alert" style={{ color: 'crimson' }}>
            {flash.error}
          </p>
        )}
      </header>
      <main>{children}</main>
    </div>
  )
}
