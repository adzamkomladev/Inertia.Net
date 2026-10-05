import { Link, usePage } from '@inertiajs/react'
import type { ReactNode } from 'react'

export default function Layout({ children }: { children: ReactNode }) {
  const { props, flash } = usePage<{ appName: string }>()
  return (
    <div style={{ fontFamily: 'sans-serif', maxWidth: 640, margin: '0 auto', padding: 16 }}>
      <header>
        <strong data-testid="app-name">{props.appName}</strong>
        <nav style={{ display: 'flex', gap: 12, margin: '8px 0' }}>
          <Link href="/" data-testid="nav-home">Home</Link>
          <Link href="/users" data-testid="nav-users">Users</Link>
          <Link href="/contacts/create" data-testid="nav-contacts">Contacts</Link>
          <Link href="/feed" data-testid="nav-feed">Feed</Link>
        </nav>
        <div data-testid="flash">{flash.success ? String(flash.success) : null}</div>
      </header>
      <main>{children}</main>
    </div>
  )
}
