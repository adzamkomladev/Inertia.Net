<script setup lang="ts">
import { Deferred, router } from '@inertiajs/vue3'
import Layout from '../../Layout.vue'

defineProps<{
  users: { id: number; name: string }[]
  stats?: { total: number }
  plans: { items: { name: string; price: number }[]; resolvedCount: number }
}>()
</script>

<template>
  <Layout>
    <h1>Users</h1>
    <ul>
      <li v-for="user in users" :key="user.id" data-testid="user-row">
        {{ user.name }}
        <button :data-testid="`delete-user-${user.id}`" @click="router.delete(`/users/${user.id}`)">Delete</button>
      </li>
    </ul>
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
