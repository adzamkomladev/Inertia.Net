<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link } from '@inertiajs/vue3'
import Layout from '../Layout.vue'

const props = defineProps<{ status: number }>()
const messages: Record<number, [string, string]> = {
  403: ['Access denied', 'You do not have permission to view this page.'],
  404: ['Page not found', 'The page you are looking for could not be found.'],
  500: ['Server error', 'Something went wrong. Please try again.'],
  503: ['Service unavailable', 'The service is temporarily unavailable. Please try again later.'],
}
const message = computed(() => messages[props.status] ?? ['Request failed', 'The request could not be completed.'])
</script>

<template>
  <Layout>
    <Head :title="`${status}: ${message[0]}`" />
    <h1><span data-testid="error-status">{{ status }}</span>: {{ message[0] }}</h1>
    <p>{{ message[1] }}</p>
    <Link href="/">Return home</Link>
  </Layout>
</template>
