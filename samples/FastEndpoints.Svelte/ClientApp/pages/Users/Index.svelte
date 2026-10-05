<script lang="ts">
  import { Deferred, router } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'

  interface Props {
    users: { id: number; name: string }[]
    stats?: { total: number }
    plans: { items: { name: string; price: number }[]; resolvedCount: number }
  }

  let { users, stats, plans }: Props = $props()
</script>

<Layout>
  <h1>Users</h1>
  <ul>
    {#each users as user (user.id)}
      <li data-testid="user-row">
        {user.name}
        <button data-testid="delete-user-{user.id}" onclick={() => router.delete(`/users/${user.id}`)}>Delete</button>
      </li>
    {/each}
  </ul>
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
