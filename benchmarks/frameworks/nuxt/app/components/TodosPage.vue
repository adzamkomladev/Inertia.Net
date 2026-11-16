<script setup lang="ts">
import type { Todo } from '#shared/types'

defineProps<{ todos: Todo[]; page: number; perPage: number }>()
</script>

<template>
  <Layout title="Todos" page="todos">
    <p>Page {{ page }}, {{ perPage }} per page</p>
    <ul>
      <li v-for="t in todos" :key="t.id" :data-todo="t.id">
        <input type="checkbox" :checked="t.done" readonly> {{ t.title }} <em>{{ t.assignee }}</em> priority {{ t.priority }}, due {{ t.due }}
      </li>
    </ul>
    <a :href="`/todos?perPage=${perPage}&page=${page + 1}`">Next page</a>
  </Layout>
</template>
