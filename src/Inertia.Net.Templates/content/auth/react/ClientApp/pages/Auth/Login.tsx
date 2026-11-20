import { useForm } from '@inertiajs/react'
import type { FormEvent } from 'react'
import Layout from '../../Layout'
import type { InertiaPages } from '../../types/inertia'
import { routes } from '../../types/routes'

export default function Login({ returnUrl, demoAccount }: InertiaPages['Auth/Login']) {
  // Precognition: each field is checked on the server when you leave it, with the same rules as the submit.
  const form = useForm({ email: '', password: '', returnUrl }).withPrecognition(routes.login.store())

  const submit = (e: FormEvent) => {
    e.preventDefault()
    form.submit(routes.login.store(), { onFinish: () => form.reset('password') })
  }

  return (
    <Layout>
      <h1>Log in</h1>
      {demoAccount && <p>Use demo@example.com and the password "password".</p>}
      <form onSubmit={submit} style={{ display: 'grid', gap: 12, maxWidth: 320 }}>
        <label>
          Email
          <input type="email" name="email" autoComplete="username" value={form.data.email}
            onChange={(e) => form.setData('email', e.target.value)} onBlur={() => form.validate('email')}
            aria-invalid={Boolean(form.errors.email)} style={{ display: 'block', width: '100%' }} />
        </label>
        {form.errors.email && <div role="alert" style={{ color: 'crimson' }}>{form.errors.email}</div>}
        <label>
          Password
          <input type="password" name="password" autoComplete="current-password" value={form.data.password}
            onChange={(e) => form.setData('password', e.target.value)} onBlur={() => form.validate('password')}
            aria-invalid={Boolean(form.errors.password)} style={{ display: 'block', width: '100%' }} />
        </label>
        {form.errors.password && <div role="alert" style={{ color: 'crimson' }}>{form.errors.password}</div>}
        <button type="submit" disabled={form.processing}>Log in</button>
      </form>
    </Layout>
  )
}
