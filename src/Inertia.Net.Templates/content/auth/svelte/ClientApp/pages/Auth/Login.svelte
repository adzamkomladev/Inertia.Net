<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'
  import type { InertiaPages } from '../../types/inertia'
  import { routes } from '../../types/routes'

  let { returnUrl, demoAccount }: InertiaPages['Auth/Login'] = $props()

  // Precognition: each field is checked on the server when you leave it, with the same rules as the submit.
  const form = useForm({ email: '', password: '', returnUrl }).withPrecognition(routes.login.store())
  const submit = (e: SubmitEvent) => {
    e.preventDefault()
    form.submit(routes.login.store(), { onFinish: () => form.reset('password') })
  }
</script>

<Layout>
  <h1>Log in</h1>
  {#if demoAccount}
    <p>Use demo@example.com and the password "password".</p>
  {/if}
  <form onsubmit={submit} style="display: grid; gap: 12px; max-width: 320px">
    <label>
      Email
      <input type="email" name="email" autocomplete="username" bind:value={form.email} onblur={() => form.validate('email')}
        aria-invalid={Boolean(form.errors.email)} style="display: block; width: 100%" />
    </label>
    {#if form.errors.email}
      <div role="alert" style="color: crimson">{form.errors.email}</div>
    {/if}
    <label>
      Password
      <input type="password" name="password" autocomplete="current-password" bind:value={form.password}
        onblur={() => form.validate('password')} aria-invalid={Boolean(form.errors.password)} style="display: block; width: 100%" />
    </label>
    {#if form.errors.password}
      <div role="alert" style="color: crimson">{form.errors.password}</div>
    {/if}
    <button type="submit" disabled={form.processing}>Log in</button>
  </form>
</Layout>
