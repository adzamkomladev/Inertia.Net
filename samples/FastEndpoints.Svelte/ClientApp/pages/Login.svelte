<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import Layout from '../Layout.svelte'

  let { submissions }: { submissions: number } = $props()

  const form = useForm({ email: '', password: '' }).withPrecognition('post', '/login')
  form.setValidationTimeout(500)

  const submit = (event: SubmitEvent) => {
    event.preventDefault()
    form.post('/login', { onFinish: () => form.reset('password') })
  }
</script>

<svelte:head>
  <title>Login validation</title>
</svelte:head>

<Layout>
  <h1>Login validation</h1>
  <p>Leave a field to check it before submitting. Use dummy values; this demo does not sign you in.</p>
  <form onsubmit={submit} style="max-width: 360px">
    <div style="margin-bottom: 16px">
      <label for="login-email" style="display: block; margin-bottom: 4px">Email</label>
      <input id="login-email" name="email" type="email" autocomplete="username" required
        bind:value={form.email} onblur={() => form.validate('email')} aria-invalid={Boolean(form.errors.email)}
        aria-describedby={form.errors.email ? 'login-email-error' : undefined}
        style="box-sizing: border-box; width: 100%; font: inherit; min-height: 40px; padding: 6px" />
      <div id="login-email-error" role="alert">{form.errors.email ?? ''}</div>
    </div>
    <div style="margin-bottom: 16px">
      <label for="login-password" style="display: block; margin-bottom: 4px">Password</label>
      <input id="login-password" name="password" type="password" autocomplete="current-password" required minlength={8}
        bind:value={form.password} onblur={() => form.validate('password')} aria-invalid={Boolean(form.errors.password)}
        aria-describedby="login-password-help login-password-error"
        style="box-sizing: border-box; width: 100%; font: inherit; min-height: 40px; padding: 6px" />
      <div id="login-password-help">At least 8 characters.</div>
      <div id="login-password-error" role="alert">{form.errors.password ?? ''}</div>
    </div>
    <p role="status" aria-live="polite">{form.validating ? 'Checking fields…' : 'Fields are checked when you leave them.'}</p>
    <button type="submit" disabled={form.processing} style="font: inherit; min-height: 40px; padding: 6px 12px">{form.processing ? 'Submitting…' : 'Submit demo'}</button>
  </form>
  <p>Accepted demo submissions: <span data-testid="login-submissions">{submissions}</span></p>
</Layout>
