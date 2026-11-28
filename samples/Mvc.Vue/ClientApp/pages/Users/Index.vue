<script setup lang="ts">
import { Deferred, router } from '@inertiajs/vue3'
import Layout from '../../Layout.vue'

defineProps<{
  users: {
    data: { id: number; name: string }[]
    currentPage: number
    lastPage: number | null
    pageName: string
  }
  stats?: { total: number }
  plans: { items: { name: string; price: number }[]; resolvedCount: number }
}>()
</script>

<template>
  <Layout>
    <h1>Users</h1>
    <ul>
      <li v-for="user in users.data" :key="user.id" data-testid="user-row">
        {{ user.name }}
        <button :data-testid="`delete-user-${user.id}`" @click="router.delete(`/users/${user.id}`)">Delete</button>
      </li>
    </ul>
    <nav data-testid="pager" aria-label="User pages">
      <button
        v-for="number in users.lastPage ?? 1"
        :key="number"
        :data-testid="`page-${number}`"
        :aria-current="number === users.currentPage ? 'page' : undefined"
        :disabled="number === users.currentPage"
        @click="router.get('/users', { [users.pageName]: number }, { only: ['users'], preserveState: true })"
      >{{ number }}</button>
    </nav>
    <button data-testid="users-refresh" @click="router.visit('/users')">Refresh</button>
    <Deferred data="stats">
      <template #fallback><p data-testid="stats-loading">Loading stats...</p></template>
      <p data-testid="stats">Total: {{ stats?.total }}</p>
    </Deferred>
    <div data-testid="plans">
      {{ plans.items.map((plan) => plan.name).join(', ') }} (resolved <span data-testid="plans-count">{{ plans.resolvedCount }}</span>)
    </div>
  </Layout>
</template>
