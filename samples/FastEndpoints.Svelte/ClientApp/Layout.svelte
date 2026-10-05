<script lang="ts">
  import { Link, page } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'

  let { children }: { children: Snippet } = $props()
</script>

<div style="font-family: sans-serif; max-width: 640px; margin: 0 auto; padding: 16px">
  <header>
    <strong data-testid="app-name">{page.props.appName}</strong>
    <nav style="display: flex; gap: 12px; margin: 8px 0">
      <Link href="/" data-testid="nav-home">Home</Link>
      <Link href="/users" data-testid="nav-users">Users</Link>
      <Link href="/contacts/create" data-testid="nav-contacts">Contacts</Link>
      <Link href="/feed" data-testid="nav-feed">Feed</Link>
    </nav>
    <div data-testid="flash">{page.flash.success ?? ''}</div>
  </header>
  <main>{@render children()}</main>
</div>
