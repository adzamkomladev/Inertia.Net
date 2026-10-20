import { Link, usePage } from '@inertiajs/react'
import type { ReactNode } from 'react'

export default function Layout({ children }: { children: ReactNode }) {
  // props.appName and flash.success are typed by types/inertia.d.ts.
  const { props, flash } = usePage()
  return (
    <div style={{ fontFamily: 'system-ui, sans-serif', maxWidth: 640, margin: '0 auto', padding: 16 }}>
      <header>
        <strong>{props.appName}</strong>
        <nav style={{ display: 'flex', gap: 12, margin: '8px 0' }}>
          <Link href="/">Home</Link>
          <Link href="/todos">Todos</Link>
        </nav>
        {flash.success && <p style={{ color: 'green' }}>{flash.success}</p>}
      </header>
      <main>{children}</main>
    </div>
  )
}
