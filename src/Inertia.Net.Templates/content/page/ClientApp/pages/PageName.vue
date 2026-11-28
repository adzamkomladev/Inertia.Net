<script setup lang="ts">
import Layout from 'PARENTS/Layout.vue'
import type { InertiaPages } from 'PARENTS/types/inertia'

defineProps<InertiaPages['PageName']>()
</script>

<template>
  <Layout>
    <h1>PageName</h1>
    <p>{{ message }}</p>
  </Layout>
</template>
