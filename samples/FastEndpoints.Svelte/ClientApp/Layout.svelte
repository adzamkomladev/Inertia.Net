<script lang="ts">
  import { Link, page } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'
  import { routes } from './types/routes'

  let { children }: { children: Snippet } = $props()
</script>

<div style="font-family: sans-serif; max-width: 640px; margin: 0 auto; padding: 16px">
  <header>
    <strong data-testid="app-name">{page.props.appName}</strong>
    <nav style="display: flex; flex-wrap: wrap; gap: 12px; margin: 8px 0">
      <Link href={routes.home()} data-testid="nav-home">Home</Link>
      <Link href={routes.users.index()} data-testid="nav-users">Users</Link>
      <Link href={routes.contacts.create()} data-testid="nav-contacts">Contacts</Link>
      <Link href={routes.feed.index()} data-testid="nav-feed">Feed</Link>
      <Link href={routes.live.index()} data-testid="nav-live">Live</Link>
      <Link href={routes.login()} data-testid="nav-login">Login</Link>
    </nav>
    <div data-testid="flash">{page.flash.success ?? ''}</div>
  </header>
  <main>{@render children()}</main>
</div>
