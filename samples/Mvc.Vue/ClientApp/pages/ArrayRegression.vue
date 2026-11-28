<script setup lang="ts">
defineProps<{ rows: { name: string; extra?: number }[] }>()
</script>

<template>
  <ul>
    <li v-for="(row, index) in rows" :key="index" data-testid="array-row">{{ row.name }}: {{ row.extra ?? 'loading' }}</li>
  </ul>
</template>
