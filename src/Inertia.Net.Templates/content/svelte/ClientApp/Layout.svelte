<script lang="ts">
  import { Link, page } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'

  let { children }: { children: Snippet } = $props()
</script>

<!-- page.props.appName and page.flash.success are typed by types/inertia.d.ts. -->
<div style="font-family: system-ui, sans-serif; max-width: 640px; margin: 0 auto; padding: 16px">
  <header>
    <strong>{page.props.appName}</strong>
    <nav style="display: flex; gap: 12px; margin: 8px 0">
      <Link href="/">Home</Link>
      <Link href="/todos">Todos</Link>
    </nav>
    {#if page.flash.success}
      <p style="color: green">{page.flash.success}</p>
    {/if}
  </header>
  <main>{@render children()}</main>
</div>
