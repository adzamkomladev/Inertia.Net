<script lang="ts">
  import { Deferred, router, useForm } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'
  import type { InertiaPages } from '../../types/inertia'
  import { routes } from '../../types/routes'

  let { todos, stats }: InertiaPages['Todos/Index'] = $props()

  const form = useForm({ title: '' })
  const submit = (e: SubmitEvent) => {
    e.preventDefault()
    form.submit(routes.todos.store(), { onSuccess: () => form.reset() })
  }
</script>

<Layout>
  <h1>Todos</h1>
  <ul>
    {#each todos as todo (todo.id)}
      <li>
        <label style:text-decoration={todo.done ? 'line-through' : undefined}>
          <input type="checkbox" checked={todo.done} onchange={() => router.visit(routes.todos.toggle(todo.id))} />
          {todo.title}
        </label>
        <button onclick={() => router.visit(routes.todos.destroy(todo.id))}>Delete</button>
      </li>
    {/each}
  </ul>
  <form onsubmit={submit}>
    <input placeholder="What needs doing?" bind:value={form.title} />
    <button type="submit" disabled={form.processing}>Add</button>
    {#if form.errors.title}
      <div style="color: crimson">{form.errors.title}</div>
    {/if}
  </form>
  <Deferred data="stats">
    {#snippet fallback()}
      <p>Counting...</p>
    {/snippet}
    <p>{stats?.done} of {stats?.total} done</p>
  </Deferred>
</Layout>
