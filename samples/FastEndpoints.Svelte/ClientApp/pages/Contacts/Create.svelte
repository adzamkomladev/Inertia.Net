<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'

  const contact = useForm({ name: '', email: '' })
  const newsletter = useForm({ email: '' })

  const submitContact = (e: SubmitEvent) => {
    e.preventDefault()
    contact.post('/contacts')
  }
  const submitNewsletter = (e: SubmitEvent) => {
    e.preventDefault()
    newsletter.post('/newsletter', { errorBag: 'newsletter' })
  }
</script>

<Layout>
  <h1>New contact</h1>
  <form onsubmit={submitContact}>
    <input data-testid="name" placeholder="Name" bind:value={contact.name} />
    <div data-testid="error-name">{contact.errors.name ?? ''}</div>
    <input data-testid="email" placeholder="Email" bind:value={contact.email} />
    <div data-testid="error-email">{contact.errors.email ?? ''}</div>
    <button data-testid="submit" type="submit" disabled={contact.processing}>Save</button>
  </form>
  <h2>Newsletter</h2>
  <form onsubmit={submitNewsletter}>
    <input data-testid="newsletter-email" placeholder="Email" bind:value={newsletter.email} />
    <div data-testid="error-newsletter-email">{newsletter.errors.email ?? ''}</div>
    <button data-testid="newsletter-submit" type="submit" disabled={newsletter.processing}>Subscribe</button>
  </form>
</Layout>
