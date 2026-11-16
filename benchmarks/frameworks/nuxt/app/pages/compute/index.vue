<script setup lang="ts">
const route = useRoute()
const { data } = await useFetch('/api/compute', { query: computed(() => route.query) })
</script>

<template>
  <ComputePage v-if="data" v-bind="data" />
</template>
