<script setup lang="ts">
import { Head, useForm } from '@inertiajs/vue3'
import Layout from '../Layout.vue'

defineProps<{ submissions: number }>()

const form = useForm({ email: '', password: '' }).withPrecognition('post', '/login')
form.setValidationTimeout(500)

const submit = () => form.post('/login', { onFinish: () => form.reset('password') })
</script>

<template>
  <Layout>
    <Head title="Login validation" />
    <h1>Login validation</h1>
    <p>Leave a field to check it before submitting. Use dummy values; this demo does not sign you in.</p>
    <form style="max-width: 360px" @submit.prevent="submit">
      <div style="margin-bottom: 16px">
        <label for="login-email" style="display: block; margin-bottom: 4px">Email</label>
        <input id="login-email" v-model="form.email" name="email" type="email" autocomplete="username" required
          :aria-invalid="Boolean(form.errors.email)" :aria-describedby="form.errors.email ? 'login-email-error' : undefined"
          style="box-sizing: border-box; width: 100%; font: inherit; min-height: 40px; padding: 6px" @blur="form.validate('email')" />
        <div id="login-email-error" role="alert">{{ form.errors.email }}</div>
      </div>
      <div style="margin-bottom: 16px">
        <label for="login-password" style="display: block; margin-bottom: 4px">Password</label>
        <input id="login-password" v-model="form.password" name="password" type="password" autocomplete="current-password" required
          minlength="8" :aria-invalid="Boolean(form.errors.password)" aria-describedby="login-password-help login-password-error"
          style="box-sizing: border-box; width: 100%; font: inherit; min-height: 40px; padding: 6px" @blur="form.validate('password')" />
        <div id="login-password-help">At least 8 characters.</div>
        <div id="login-password-error" role="alert">{{ form.errors.password }}</div>
      </div>
      <p role="status" aria-live="polite">{{ form.validating ? 'Checking fields…' : 'Fields are checked when you leave them.' }}</p>
      <button type="submit" :disabled="form.processing" style="font: inherit; min-height: 40px; padding: 6px 12px">
        {{ form.processing ? 'Submitting…' : 'Submit demo' }}
      </button>
    </form>
    <p>Accepted demo submissions: <span data-testid="login-submissions">{{ submissions }}</span></p>
  </Layout>
</template>
