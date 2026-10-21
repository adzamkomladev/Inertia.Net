<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3'

// page.props.appName and page.flash.success are typed by types/inertia.d.ts.
const page = usePage()
</script>

<template>
  <div style="font-family: system-ui, sans-serif; max-width: 640px; margin: 0 auto; padding: 16px">
    <header>
      <strong>{{ page.props.appName }}</strong>
      <nav style="display: flex; gap: 12px; margin: 8px 0">
        <Link href="/">Home</Link>
        <Link href="/todos">Todos</Link>
      </nav>
      <p v-if="page.flash.success" style="color: green">{{ page.flash.success }}</p>
    </header>
    <main><slot /></main>
  </div>
</template>
