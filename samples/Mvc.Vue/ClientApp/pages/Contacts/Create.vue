<script setup lang="ts">
import { Link, useForm, useHttp } from '@inertiajs/vue3'
import Layout from '../../Layout.vue'
import { routes } from '../../types/routes'

const contact = useForm({ name: '', email: '' })
const newsletter = useForm({ email: '' })
// useHttp: a plain JSON request (no page visit); validation errors come back as a 422 and land in check.errors.
const check = useHttp({ name: '', email: '' })
</script>

<template>
  <Layout>
    <h1>New contact</h1>
    <Link :href="routes.home()" instant>Back to home</Link>
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
    <h2>Check a contact</h2>
    <form @submit.prevent="check.post('/contacts/check').catch(() => {})">
      <input v-model="check.name" data-testid="http-name" placeholder="Name" />
      <div data-testid="http-error-name">{{ check.errors.name }}</div>
      <input v-model="check.email" data-testid="http-email" placeholder="Email" />
      <div data-testid="http-error-email">{{ check.errors.email }}</div>
      <button data-testid="http-submit" type="submit" :disabled="check.processing">Check</button>
      <p v-if="check.wasSuccessful" data-testid="http-ok">Looks good</p>
    </form>
  </Layout>
</template>
