<script setup lang="ts">
import type { Stats } from '#shared/types'

defineProps<{ stats: Stats }>()
</script>

<template>
  <Layout title="Dashboard" page="dashboard">
    <p>{{ stats.total }} todos, {{ stats.done }} done</p>
    <table>
      <thead>
        <tr><th>Assignee</th><th>Total</th><th>Done</th><th>Priority sum</th></tr>
      </thead>
      <tbody>
        <tr v-for="a in stats.byAssignee" :key="a.assignee"><td>{{ a.assignee }}</td><td>{{ a.total }}</td><td>{{ a.done }}</td><td>{{ a.prioritySum }}</td></tr>
      </tbody>
    </table>
  </Layout>
</template>
