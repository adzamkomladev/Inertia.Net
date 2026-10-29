import { Link } from '@inertiajs/react'
import { ArrowLeft, ArrowRight, Pencil } from 'lucide-react'
import { getAdjacent, type DocPage } from '../lib/content'

export function PageFooter({ page }: { page: DocPage }) {
  const { previous, next } = getAdjacent(page.slug)

  return (
    <footer className="mt-14 border-t border-border pt-6">
      <a
        href={`https://github.com/adzamkomladev/Inertia.Net/edit/main/docs/Inertia.Net.Docs/content/docs/${page.slug}.mdx`}
        target="_blank"
        rel="noreferrer"
        className="inline-flex items-center gap-1.5 text-xs text-muted-foreground transition-colors hover:text-foreground"
      >
        <Pencil className="size-3" />
        Edit this page on GitHub
      </a>

      <div className="mt-6 grid gap-3 sm:grid-cols-2">
        {previous ? (
          <Link
            href={previous.url}
            className="group flex flex-col rounded-xl border border-border bg-card p-4 transition-colors hover:border-primary/35 hover:bg-accent/40"
          >
            <span className="flex items-center gap-1 text-[11px] font-medium uppercase tracking-wider text-muted-foreground">
              <ArrowLeft className="size-3 transition-transform group-hover:-translate-x-0.5" /> Previous
            </span>
            <span className="mt-1 text-sm font-medium">{previous.title}</span>
          </Link>
        ) : (
          <span />
        )}
        {next ? (
          <Link
            href={next.url}
            className="group flex flex-col rounded-xl border border-border bg-card p-4 text-right transition-colors hover:border-primary/35 hover:bg-accent/40 sm:col-start-2"
          >
            <span className="flex items-center justify-end gap-1 text-[11px] font-medium uppercase tracking-wider text-muted-foreground">
              Next <ArrowRight className="size-3 transition-transform group-hover:translate-x-0.5" />
            </span>
            <span className="mt-1 text-sm font-medium">{next.title}</span>
          </Link>
        ) : null}
      </div>
    </footer>
  )
}
