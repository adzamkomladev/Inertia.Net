<script setup lang="ts">
import Layout from '../Layout.vue'
import type { InertiaPages } from '../types/inertia'

defineProps<InertiaPages['Dashboard']>()
</script>

<template>
  <Layout>
    <h1>Dashboard</h1>
    <p>You are signed in as {{ name }}.</p>
  </Layout>
</template>
