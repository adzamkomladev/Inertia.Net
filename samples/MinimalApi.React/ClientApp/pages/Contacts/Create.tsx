import { useForm } from '@inertiajs/react'
import type { FormEvent } from 'react'
import Layout from '../../Layout'

export default function Create() {
  const contact = useForm({ name: '', email: '' })
  const newsletter = useForm({ email: '' })

  const submitContact = (e: FormEvent) => {
    e.preventDefault()
    contact.post('/contacts')
  }
  const submitNewsletter = (e: FormEvent) => {
    e.preventDefault()
    newsletter.post('/newsletter', { errorBag: 'newsletter' })
  }

  return (
    <Layout>
      <h1>New contact</h1>
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
    </Layout>
  )
}
