<!-- shared/ts/NewTodoPage.tsx in Vue: the form without data fetching; pages/todos/new.vue owns the state and the post. -->
<script setup lang="ts">
import { ASSIGNEES } from '#shared/rules'
import type { FormErrors, NewTodo } from '#shared/types'

defineProps<{ errors: FormErrors; processing: boolean }>()
const values = defineModel<NewTodo>('values', { required: true })
const emit = defineEmits<{ submit: [] }>()
</script>

<template>
  <Layout title="New todo" page="new">
    <form novalidate @submit.prevent="emit('submit')">
      <label>Title <input v-model="values.title" name="title"></label>
      <p v-if="errors.title" class="error" data-error="title">{{ errors.title }}</p>
      <label>Priority <input v-model.number="values.priority" name="priority" type="number" min="0" max="4"></label>
      <p v-if="errors.priority" class="error" data-error="priority">{{ errors.priority }}</p>
      <label>Assignee <select v-model="values.assignee" name="assignee"><option v-for="a in ASSIGNEES" :key="a">{{ a }}</option></select></label>
      <p v-if="errors.assignee" class="error" data-error="assignee">{{ errors.assignee }}</p>
      <label>Due <input v-model="values.due" name="due" type="date"></label>
      <p v-if="errors.due" class="error" data-error="due">{{ errors.due }}</p>
      <button type="submit" :disabled="processing">Add todo</button>
    </form>
  </Layout>
</template>
