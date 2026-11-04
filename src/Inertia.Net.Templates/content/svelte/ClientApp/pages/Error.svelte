<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import Layout from '../Layout.svelte'

  let { status }: { status: number } = $props()
  const messages: Record<number, [string, string]> = {
    403: ['Access denied', 'You do not have permission to view this page.'],
    404: ['Page not found', 'The page you are looking for could not be found.'],
    500: ['Server error', 'Something went wrong. Please try again.'],
    503: ['Service unavailable', 'The service is temporarily unavailable. Please try again later.'],
  }
  const message = $derived(messages[status] ?? ['Request failed', 'The request could not be completed.'])
</script>

<svelte:head>
  <title>{status}: {message[0]}</title>
</svelte:head>

<Layout>
  <h1><span>{status}</span>: {message[0]}</h1>
  <p>{message[1]}</p>
  <Link href="/">Return home</Link>
</Layout>
