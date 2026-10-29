import { Head, Link } from '@inertiajs/react'
import { ChevronRight } from 'lucide-react'
import { useMemo } from 'react'
import { DocsLayout } from '../../components/DocsLayout'
import { mdxComponents } from '../../components/Mdx'
import { PageFooter } from '../../components/PageFooter'
import { getHeadings, getPage } from '../../lib/content'
import { getDocModule } from '../../lib/page-content'

export default function Show({ slug }: { slug: string }) {
  const page = getPage(slug)
  const module = getDocModule(slug)
  const headings = useMemo(() => getHeadings(slug), [slug])

  if (!page || !module) {
    return (
      <div className="mx-auto max-w-xl px-6 py-24 text-center">
        <p className="text-sm font-medium text-primary">404</p>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight">Page not found</h1>
        <p className="mt-3 text-muted-foreground">The documentation page “{slug}” does not exist.</p>
        <Link href="/docs/introduction" className="mt-6 inline-flex text-sm font-medium text-primary hover:underline">
          Back to the documentation
        </Link>
      </div>
    )
  }

  const Content = module.default

  return (
    <>
      <Head title={`${page.title} — Inertia.Net Docs`}>
        <meta name="description" head-key="description" content={page.description ?? `Inertia.Net documentation: ${page.title}`} />
      </Head>
      <DocsLayout page={page} headings={headings}>
        <nav className="mb-6 flex items-center gap-1.5 text-xs text-muted-foreground" aria-label="Breadcrumb">
          <Link href="/docs/introduction" className="transition-colors hover:text-foreground">
            Docs
          </Link>
          <ChevronRight className="size-3" />
          <span>{page.section}</span>
        </nav>

        <header className="mb-9 border-b border-border pb-8">
          <h1 className="text-balance text-3xl font-semibold tracking-[-0.03em] sm:text-4xl">{page.title}</h1>
          {page.description && <p className="mt-3 text-pretty text-[15px] leading-7 text-muted-foreground sm:text-base">{page.description}</p>}
        </header>

        <div className="prose prose-neutral max-w-none dark:prose-invert prose-p:leading-7 prose-li:leading-7">
          <Content components={mdxComponents} />
        </div>

        <PageFooter page={page} />
      </DocsLayout>
    </>
  )
}
