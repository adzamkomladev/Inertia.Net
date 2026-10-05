<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3'

const page = usePage<{ appName: string }>()
</script>

<template>
  <div style="font-family: sans-serif; max-width: 640px; margin: 0 auto; padding: 16px">
    <header>
      <strong data-testid="app-name">{{ page.props.appName }}</strong>
      <nav style="display: flex; gap: 12px; margin: 8px 0">
        <Link href="/" data-testid="nav-home">Home</Link>
        <Link href="/users" data-testid="nav-users">Users</Link>
        <Link href="/contacts/create" data-testid="nav-contacts">Contacts</Link>
        <Link href="/feed" data-testid="nav-feed">Feed</Link>
      </nav>
      <div data-testid="flash">{{ page.flash.success ? String(page.flash.success) : '' }}</div>
    </header>
    <main><slot /></main>
  </div>
</template>
