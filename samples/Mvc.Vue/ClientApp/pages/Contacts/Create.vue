<script setup lang="ts">
import { useForm } from '@inertiajs/vue3'
import Layout from '../../Layout.vue'

const contact = useForm({ name: '', email: '' })
const newsletter = useForm({ email: '' })
</script>

<template>
  <Layout>
    <h1>New contact</h1>
    <form @submit.prevent="contact.post('/contacts')">
      <input v-model="contact.name" data-testid="name" placeholder="Name" />
      <div data-testid="error-name">{{ contact.errors.name }}</div>
      <input v-model="contact.email" data-testid="email" placeholder="Email" />
      <div data-testid="error-email">{{ contact.errors.email }}</div>
      <button data-testid="submit" type="submit" :disabled="contact.processing">Save</button>
    </form>
    <h2>Newsletter</h2>
    <form @submit.prevent="newsletter.post('/newsletter', { errorBag: 'newsletter' })">
      <input v-model="newsletter.email" data-testid="newsletter-email" placeholder="Email" />
      <div data-testid="error-newsletter-email">{{ newsletter.errors.email }}</div>
      <button data-testid="newsletter-submit" type="submit" :disabled="newsletter.processing">Subscribe</button>
    </form>
  </Layout>
</template>
