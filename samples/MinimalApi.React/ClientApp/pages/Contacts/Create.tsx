import { Link, useForm, useHttp } from '@inertiajs/react'
import type { FormEvent } from 'react'
import Layout from '../../Layout'
import { routes } from '../../types/routes'

export default function Create() {
  const contact = useForm({ name: '', email: '' })
  const newsletter = useForm({ email: '' })
  // useHttp: a plain JSON request (no page visit); validation errors come back as a 422 and land in check.errors.
  const check = useHttp({ name: '', email: '' })

  const submitContact = (e: FormEvent) => {
    e.preventDefault()
    contact.post('/contacts')
  }
  const submitNewsletter = (e: FormEvent) => {
    e.preventDefault()
    newsletter.post('/newsletter', { errorBag: 'newsletter' })
  }
  const submitCheck = (e: FormEvent) => {
    e.preventDefault()
    void check.post('/contacts/check')
  }

  return (
    <Layout>
      <h1>New contact</h1>
      <Link href={routes.home()} instant>Back to home</Link>
      <form onSubmit={submitContact}>
        <input data-testid="name" placeholder="Name" value={contact.data.name} onChange={(e) => contact.setData('name', e.target.value)} />
        <div data-testid="error-name">{contact.errors.name}</div>
        <input data-testid="email" placeholder="Email" value={contact.data.email} onChange={(e) => contact.setData('email', e.target.value)} />
        <div data-testid="error-email">{contact.errors.email}</div>
        <button data-testid="submit" type="submit" disabled={contact.processing}>Save</button>
      </form>
      <h2>Newsletter</h2>
      <form onSubmit={submitNewsletter}>
        <input data-testid="newsletter-email" placeholder="Email" value={newsletter.data.email} onChange={(e) => newsletter.setData('email', e.target.value)} />
        <div data-testid="error-newsletter-email">{newsletter.errors.email}</div>
        <button data-testid="newsletter-submit" type="submit" disabled={newsletter.processing}>Subscribe</button>
      </form>
      <h2>Check a contact</h2>
      <form onSubmit={submitCheck}>
        <input data-testid="http-name" placeholder="Name" value={check.data.name} onChange={(e) => check.setData('name', e.target.value)} />
        <div data-testid="http-error-name">{check.errors.name}</div>
        <input data-testid="http-email" placeholder="Email" value={check.data.email} onChange={(e) => check.setData('email', e.target.value)} />
        <div data-testid="http-error-email">{check.errors.email}</div>
        <button data-testid="http-submit" type="submit" disabled={check.processing}>Check</button>
        {check.wasSuccessful && <p data-testid="http-ok">Looks good</p>}
      </form>
    </Layout>
  )
}
