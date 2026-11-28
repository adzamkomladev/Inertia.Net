<script lang="ts">
  import { Deferred, router } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'

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

  let { users, stats, plans }: Props = $props()
  const pages = $derived(Array.from({ length: users.lastPage ?? 1 }, (_, index) => index + 1))
</script>

<Layout>
  <h1>Users</h1>
  <ul>
    {#each users.data as user (user.id)}
      <li data-testid="user-row">
        {user.name}
        <button data-testid="delete-user-{user.id}" onclick={() => router.delete(`/users/${user.id}`)}>Delete</button>
      </li>
    {/each}
  </ul>
  <nav data-testid="pager" aria-label="User pages">
    {#each pages as number (number)}
      <button
        data-testid="page-{number}"
        aria-current={number === users.currentPage ? 'page' : undefined}
        disabled={number === users.currentPage}
        onclick={() => router.get('/users', { [users.pageName]: number }, { only: ['users'], preserveState: true })}
      >{number}</button>
    {/each}
  </nav>
  <button data-testid="users-refresh" onclick={() => router.visit('/users')}>Refresh</button>
  <Deferred data="stats">
    {#snippet fallback()}
      <p data-testid="stats-loading">Loading stats...</p>
    {/snippet}
    <p data-testid="stats">Total: {stats?.total}</p>
  </Deferred>
  <div data-testid="plans">
    {plans.items.map((plan) => plan.name).join(', ')} (resolved <span data-testid="plans-count">{plans.resolvedCount}</span>)
  </div>
</Layout>
