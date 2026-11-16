<script setup lang="ts">
const { data } = await useFetch('/api/dashboard')
</script>

<template>
  <DashboardPage v-if="data" v-bind="data" />
</template>
