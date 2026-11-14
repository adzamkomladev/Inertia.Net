<script setup lang="ts">
import { Link, router, usePage } from '@inertiajs/vue3'
import { computed } from 'vue'
import { routes } from './types/routes'

// page.props.appName, page.props.auth and page.flash are typed by types/inertia.d.ts.
const page = usePage()
const user = computed(() => page.props.auth.user)
</script>

<template>
  <div style="font-family: system-ui, sans-serif; max-width: 640px; margin: 0 auto; padding: 16px">
    <header>
      <strong>{{ page.props.appName }}</strong>
      <nav style="display: flex; gap: 12px; margin: 8px 0">
        <Link :href="routes.home()">Home</Link>
        <Link :href="routes.todos.index()">Todos</Link>
        <template v-if="user">
          <Link :href="routes.dashboard()">Dashboard</Link>
          <span style="margin-left: auto">{{ user.name }}</span>
          <button @click="router.visit(routes.logout())">Log out</button>
        </template>
        <Link v-else :href="routes.login()" style="margin-left: auto">Log in</Link>
      </nav>
      <p v-if="page.flash.success" style="color: green">{{ page.flash.success }}</p>
      <p v-if="page.flash.error" role="alert" style="color: crimson">{{ page.flash.error }}</p>
    </header>
    <main><slot /></main>
  </div>
</template>
