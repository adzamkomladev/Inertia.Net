<script setup lang="ts">
import type { FormErrors, NewTodo } from '#shared/types'

const emptyTodo: NewTodo = { title: '', priority: 2, assignee: 'ada', due: '2026-06-01' }
const values = ref<NewTodo>({ ...emptyTodo })
const errors = ref<FormErrors>({})
const processing = ref(false)

// Posts the form as JSON to the /api/todos route: a 422 shows its errors, a 201 starts a new todo.
async function submit() {
  processing.value = true
  try {
    const response = await $fetch.raw('/api/todos', { method: 'POST', body: values.value, ignoreResponseError: true })
    if (response.status === 422) errors.value = (response._data as { errors: FormErrors }).errors
    else if (response.ok) (values.value = { ...emptyTodo }), (errors.value = {})
  } finally {
    processing.value = false
  }
}
</script>

<template>
  <NewTodoPage v-model:values="values" :errors="errors" :processing="processing" @submit="submit" />
</template>
