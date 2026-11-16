<script setup lang="ts">
import type { Feature } from '#shared/types'

defineProps<{ features: Feature[] }>()
</script>

<template>
  <Layout title="Home" page="home">
    <ul>
      <li v-for="f in features" :key="f.title"><strong>{{ f.title }}</strong> {{ f.body }}</li>
    </ul>
  </Layout>
</template>
