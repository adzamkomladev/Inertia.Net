<script setup lang="ts">
import { useForm } from '@inertiajs/vue3'
import Layout from '../../Layout.vue'
import type { InertiaPages } from '../../types/inertia'
import { routes } from '../../types/routes'

const props = defineProps<InertiaPages['Auth/Login']>()

// Precognition: each field is checked on the server when you leave it, with the same rules as the submit.
const form = useForm({ email: '', password: '', returnUrl: props.returnUrl }).withPrecognition(routes.login.store())
const submit = () => form.submit(routes.login.store(), { onFinish: () => form.reset('password') })
</script>

<template>
  <Layout>
    <h1>Log in</h1>
    <p>Use demo@example.com and the password "password".</p>
    <form @submit.prevent="submit" style="display: grid; gap: 12px; max-width: 320px">
      <label>
        Email
        <input v-model="form.email" type="email" name="email" autocomplete="username" @blur="form.validate('email')"
          :aria-invalid="Boolean(form.errors.email)" style="display: block; width: 100%" />
      </label>
      <div v-if="form.errors.email" role="alert" style="color: crimson">{{ form.errors.email }}</div>
      <label>
        Password
        <input v-model="form.password" type="password" name="password" autocomplete="current-password" @blur="form.validate('password')"
          :aria-invalid="Boolean(form.errors.password)" style="display: block; width: 100%" />
      </label>
      <div v-if="form.errors.password" role="alert" style="color: crimson">{{ form.errors.password }}</div>
      <button type="submit" :disabled="form.processing">Log in</button>
    </form>
  </Layout>
</template>
