<script lang="ts">
  import { Link, useForm, useHttp } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'
  import { routes } from '../../types/routes'

  const contact = useForm({ name: '', email: '' })
  const newsletter = useForm({ email: '' })
  // useHttp: a plain JSON request (no page visit); validation errors come back as a 422 and land in check.errors.
  const check = useHttp({ name: '', email: '' })

  const submitContact = (e: SubmitEvent) => {
    e.preventDefault()
    contact.post('/contacts')
  }
  const submitNewsletter = (e: SubmitEvent) => {
    e.preventDefault()
    newsletter.post('/newsletter', { errorBag: 'newsletter' })
  }
  const submitCheck = (e: SubmitEvent) => {
    e.preventDefault()
    void check.post('/contacts/check')
  }
</script>

<Layout>
  <h1>New contact</h1>
  <Link href={routes.home()} instant>Back to home</Link>
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
  <h2>Check a contact</h2>
  <form onsubmit={submitCheck}>
    <input data-testid="http-name" placeholder="Name" bind:value={check.name} />
    <div data-testid="http-error-name">{check.errors.name ?? ''}</div>
    <input data-testid="http-email" placeholder="Email" bind:value={check.email} />
    <div data-testid="http-error-email">{check.errors.email ?? ''}</div>
    <button data-testid="http-submit" type="submit" disabled={check.processing}>Check</button>
    {#if check.wasSuccessful}
      <p data-testid="http-ok">Looks good</p>
    {/if}
  </form>
</Layout>
