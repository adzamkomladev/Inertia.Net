import { Deferred, router } from '@inertiajs/react'
import Layout from '../../Layout'

interface Props {
  users: { id: number; name: string }[]
  stats?: { total: number }
  plans: { items: { name: string; price: number }[]; resolvedCount: number }
}

export default function Index({ users, stats, plans }: Props) {
  return (
    <Layout>
      <h1>Users</h1>
      <ul>
        {users.map((user) => (
          <li key={user.id} data-testid="user-row">
            {user.name}{' '}
            <button data-testid={`delete-user-${user.id}`} onClick={() => router.delete(`/users/${user.id}`)}>Delete</button>
          </li>
        ))}
      </ul>
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
