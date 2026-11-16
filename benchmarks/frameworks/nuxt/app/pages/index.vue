<script setup lang="ts">
const { data } = await useFetch('/api/home')
</script>

<template>
  <HomePage v-if="data" v-bind="data" />
</template>
