<script setup lang="ts">
import { Deferred, router, useForm } from '@inertiajs/vue3'
import Layout from '../../Layout.vue'
import type { InertiaPages } from '../../types/inertia'
import { routes } from '../../types/routes'

defineProps<InertiaPages['Todos/Index']>()

const form = useForm({ title: '' })
const submit = () => form.submit(routes.todos.store(), { onSuccess: () => form.reset() })
</script>

<template>
  <Layout>
    <h1>Todos</h1>
    <ul>
      <li v-for="todo in todos" :key="todo.id">
        <label :style="{ textDecoration: todo.done ? 'line-through' : undefined }">
          <input type="checkbox" :checked="todo.done" @change="router.visit(routes.todos.toggle(todo.id))" /> {{ todo.title }}
        </label>
        <button @click="router.visit(routes.todos.destroy(todo.id))">Delete</button>
      </li>
    </ul>
    <form @submit.prevent="submit">
      <input v-model="form.title" placeholder="What needs doing?" />
      <button type="submit" :disabled="form.processing">Add</button>
      <div v-if="form.errors.title" style="color: crimson">{{ form.errors.title }}</div>
    </form>
    <Deferred data="stats">
      <template #fallback><p>Counting...</p></template>
      <p>{{ stats?.done }} of {{ stats?.total }} done</p>
    </Deferred>
  </Layout>
</template>
