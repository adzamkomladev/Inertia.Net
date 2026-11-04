import { Deferred, router } from '@inertiajs/react'
import Layout from '../../Layout'

interface Props {
  users: {
    data: { id: number; name: string }[]
    currentPage: number
    lastPage: number | null
    pageName: string
  }
  stats?: { total: number }
  plans: { items: { name: string; price: number }[]; resolvedCount: number }
}

export default function Index({ users, stats, plans }: Props) {
  return (
    <Layout>
      <h1>Users</h1>
      <ul>
        {users.data.map((user) => (
          <li key={user.id} data-testid="user-row">
            {user.name}{' '}
            <button data-testid={`delete-user-${user.id}`} onClick={() => router.delete(`/users/${user.id}`)}>Delete</button>
          </li>
        ))}
      </ul>
      <nav data-testid="pager" aria-label="User pages">
        {Array.from({ length: users.lastPage ?? 1 }, (_, index) => index + 1).map((number) => (
          <button
            key={number}
            data-testid={`page-${number}`}
            aria-current={number === users.currentPage ? 'page' : undefined}
            disabled={number === users.currentPage}
            onClick={() => router.get('/users', { [users.pageName]: number }, { only: ['users'], preserveState: true })}
          >{number}</button>
        ))}
      </nav>
      <button data-testid="users-refresh" onClick={() => router.visit('/users')}>Refresh</button>
      <Deferred data="stats" fallback={<p data-testid="stats-loading">Loading stats...</p>}>
        <p data-testid="stats">Total: {stats?.total}</p>
      </Deferred>
      <div data-testid="plans">
        {plans.items.map((plan) => plan.name).join(', ')} (resolved <span data-testid="plans-count">{plans.resolvedCount}</span>)
      </div>
    </Layout>
  )
}
