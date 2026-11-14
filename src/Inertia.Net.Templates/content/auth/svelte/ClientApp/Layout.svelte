<script lang="ts">
  import { Link, page, router } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'
  import { routes } from './types/routes'

  let { children }: { children: Snippet } = $props()
  const user = $derived(page.props.auth.user)
</script>

<!-- page.props.appName, page.props.auth and page.flash are typed by types/inertia.d.ts. -->
<div style="font-family: system-ui, sans-serif; max-width: 640px; margin: 0 auto; padding: 16px">
  <header>
    <strong>{page.props.appName}</strong>
    <nav style="display: flex; gap: 12px; margin: 8px 0">
      <Link href={routes.home()}>Home</Link>
      <Link href={routes.todos.index()}>Todos</Link>
      {#if user}
        <Link href={routes.dashboard()}>Dashboard</Link>
        <span style="margin-left: auto">{user.name}</span>
        <button onclick={() => router.visit(routes.logout())}>Log out</button>
      {:else}
        <Link href={routes.login()} style="margin-left: auto">Log in</Link>
      {/if}
    </nav>
    {#if page.flash.success}
      <p style="color: green">{page.flash.success}</p>
    {/if}
    {#if page.flash.error}
      <p role="alert" style="color: crimson">{page.flash.error}</p>
    {/if}
  </header>
  <main>{@render children()}</main>
</div>
