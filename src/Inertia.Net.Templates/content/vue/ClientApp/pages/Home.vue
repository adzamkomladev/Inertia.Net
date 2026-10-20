<script setup lang="ts">
import { Link } from '@inertiajs/vue3'
import Layout from '../Layout.vue'
import type { InertiaPages } from '../types/inertia'

defineProps<InertiaPages['Home']>()
</script>

<template>
  <Layout>
    <h1>Home</h1>
    <p>{{ message }}</p>
    <Link href="/todos">See the todos</Link>
  </Layout>
</template>
