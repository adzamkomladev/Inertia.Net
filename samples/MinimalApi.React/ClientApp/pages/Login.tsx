import { Head, useForm } from '@inertiajs/react'
import { useEffect, type FormEvent } from 'react'
import Layout from '../Layout'

export default function Login({ submissions }: { submissions: number }) {
  const form = useForm({ email: '', password: '' }).withPrecognition('post', '/login')

  useEffect(() => {
    form.setValidationTimeout(500)
  }, [])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    form.post('/login', { onFinish: () => form.reset('password') })
  }

  return (
    <Layout>
      <Head title="Login validation" />
      <h1>Login validation</h1>
      <p>Leave a field to check it before submitting. Use dummy values; this demo does not sign you in.</p>
      <form onSubmit={submit} style={{ maxWidth: 360 }}>
        <div style={{ marginBottom: 16 }}>
          <label htmlFor="login-email" style={{ display: 'block', marginBottom: 4 }}>Email</label>
          <input id="login-email" name="email" type="email" autoComplete="username" required
            value={form.data.email} onChange={(event) => form.setData('email', event.target.value)}
            onBlur={() => form.validate('email')} aria-invalid={Boolean(form.errors.email)}
            aria-describedby={form.errors.email ? 'login-email-error' : undefined}
            style={{ boxSizing: 'border-box', width: '100%', font: 'inherit', minHeight: 40, padding: 6 }} />
          <div id="login-email-error" role="alert">{form.errors.email}</div>
        </div>
        <div style={{ marginBottom: 16 }}>
          <label htmlFor="login-password" style={{ display: 'block', marginBottom: 4 }}>Password</label>
          <input id="login-password" name="password" type="password" autoComplete="current-password" required minLength={8}
            value={form.data.password} onChange={(event) => form.setData('password', event.target.value)}
            onBlur={() => form.validate('password')} aria-invalid={Boolean(form.errors.password)}
            aria-describedby="login-password-help login-password-error"
            style={{ boxSizing: 'border-box', width: '100%', font: 'inherit', minHeight: 40, padding: 6 }} />
          <div id="login-password-help">At least 8 characters.</div>
          <div id="login-password-error" role="alert">{form.errors.password}</div>
        </div>
        <p role="status" aria-live="polite">{form.validating ? 'Checking fields…' : 'Fields are checked when you leave them.'}</p>
        <button type="submit" disabled={form.processing} style={{ font: 'inherit', minHeight: 40, padding: '6px 12px' }}>{form.processing ? 'Submitting…' : 'Submit demo'}</button>
      </form>
      <p>Accepted demo submissions: <span data-testid="login-submissions">{submissions}</span></p>
    </Layout>
  )
}
