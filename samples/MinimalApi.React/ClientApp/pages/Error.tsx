import { Head, Link } from '@inertiajs/react'
import Layout from '../Layout'

const messages: Record<number, [string, string]> = {
  403: ['Access denied', 'You do not have permission to view this page.'],
  429: ['Too many requests', 'Please wait a minute before submitting again.'],
  404: ['Page not found', 'The page you are looking for could not be found.'],
  500: ['Server error', 'Something went wrong. Please try again.'],
  503: ['Service unavailable', 'The service is temporarily unavailable. Please try again later.'],
}

export default function Error({ status }: { status: number }) {
  const [title, description] = messages[status] ?? ['Request failed', 'The request could not be completed.']
  return (
    <Layout>
      <Head title={`${status}: ${title}`} />
      <h1><span data-testid="error-status">{status}</span>: {title}</h1>
      <p>{description}</p>
      <Link href="/">Return home</Link>
    </Layout>
  )
}
