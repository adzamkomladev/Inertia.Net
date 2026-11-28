<script setup lang="ts">
import Index from './Index.vue'

// The cursor feed renders like the numbered one; only the server's paginator differs.
defineProps<{ posts: { data: { id: number; title: string }[] } }>()
</script>

<template>
  <Index :posts="posts" />
</template>
