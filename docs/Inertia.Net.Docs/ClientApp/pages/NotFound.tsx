import { Link } from '@inertiajs/react'
import { ArrowLeft } from 'lucide-react'
import { Seo } from '../components/Seo'
import { SiteHeader } from '../components/SiteHeader'

export default function NotFound() {
  return (
    <div className="min-h-screen">
      <Seo
        title="Page not found — Inertia.Net"
        description="The requested page is not part of the Inertia.Net documentation."
        noIndex
      />
      <SiteHeader />
      <main>
        <div className="relative mx-auto flex min-h-[70vh] max-w-xl flex-col items-center justify-center px-6 py-20 text-center">
          <p className="font-mono text-sm font-medium text-primary">404</p>
          <h1 className="mt-3 text-4xl font-semibold tracking-tight">Page not found</h1>
          <p className="mt-4 text-[15px] leading-7 text-muted-foreground">
            The URL you visited is not part of the documentation. Try the introduction, or search with <kbd className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-xs">⌘K</kbd>.
          </p>
          <div className="mt-7 flex flex-wrap justify-center gap-3">
            <Link
              href="/docs/introduction"
              className="inline-flex h-10 items-center gap-2 rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
            >
              <ArrowLeft className="size-4" />
              Back to the docs
            </Link>
            <Link href="/" className="inline-flex h-10 items-center rounded-lg border border-border px-4 text-sm font-medium transition-colors hover:bg-accent">
              Home
            </Link>
          </div>
        </div>
      </main>
    </div>
  )
}
