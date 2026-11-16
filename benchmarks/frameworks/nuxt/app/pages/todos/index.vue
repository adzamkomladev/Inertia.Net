<script setup lang="ts">
const route = useRoute()
const { data } = await useFetch('/api/todos', { query: computed(() => route.query) })
</script>

<template>
  <TodosPage v-if="data" v-bind="data" />
</template>
