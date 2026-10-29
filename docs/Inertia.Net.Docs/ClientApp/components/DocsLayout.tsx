import { useState, type ReactNode } from 'react'
import type { DocPage, Heading } from '../lib/content'
import { DocsSidebar } from './DocsSidebar'
import { MobileNav } from './MobileNav'
import { SiteHeader } from './SiteHeader'
import { TableOfContents } from './TableOfContents'

export function DocsLayout({ page, headings, children }: { page: DocPage; headings: Heading[]; children: ReactNode }) {
  const [navOpen, setNavOpen] = useState(false)

  return (
    <div className="min-h-screen">
      <SiteHeader onOpenNav={() => setNavOpen(true)} />
      <div className="mx-auto flex max-w-[1440px] gap-6 px-4 sm:px-6 lg:gap-10 lg:px-8">
        <DocsSidebar />
        <main className="min-w-0 flex-1 py-8 lg:py-10">
          <article className="mx-auto max-w-3xl" data-page={page.slug}>
            {children}
          </article>
        </main>
        <TableOfContents headings={headings} />
      </div>
      <MobileNav open={navOpen} onOpenChange={setNavOpen} />
    </div>
  )
}
