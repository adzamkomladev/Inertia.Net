import { Link } from '@inertiajs/react'
import { Menu, Search } from 'lucide-react'
import { useState } from 'react'
import { Brand } from './Brand'
import { GithubIcon } from './icons/GithubIcon'
import { SearchDialog } from './SearchDialog'
import { ThemeToggle } from './ThemeToggle'
import { Kbd } from './ui/kbd'

export function SiteHeader({ onOpenNav }: { onOpenNav?: () => void }) {
  const [searchOpen, setSearchOpen] = useState(false)

  return (
    <>
      <header className="sticky top-0 z-40 border-b border-border bg-background">
        <div className="mx-auto flex h-16 max-w-[1440px] items-center gap-3 px-4 sm:px-6 lg:px-8">
          {onOpenNav && (
            <button
              type="button"
              onClick={onOpenNav}
              className="-ml-1 inline-flex size-9 items-center justify-center rounded-lg text-muted-foreground hover:bg-accent hover:text-foreground lg:hidden"
              aria-label="Open navigation"
            >
              <Menu className="size-5" />
            </button>
          )}
          <Brand />
          <nav className="ml-4 hidden items-center gap-1 text-sm sm:flex">
            <Link href="/docs/introduction" className="rounded-md px-2.5 py-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">
              Docs
            </Link>
            <Link href="/docs/ai-agents" className="rounded-md px-2.5 py-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">
              AI agents
            </Link>
            <a
              href="https://github.com/adzamkomladev/Inertia.Net-Samples"
              target="_blank"
              rel="noreferrer"
              className="rounded-md px-2.5 py-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            >
              Samples
            </a>
          </nav>

          <div className="ml-auto flex items-center gap-1.5">
            <button
              type="button"
              onClick={() => setSearchOpen(true)}
              className="hidden h-9 w-48 items-center gap-2 rounded-md border border-border px-3 text-left text-sm text-muted-foreground transition-colors hover:bg-accent md:flex xl:w-56"
            >
              <Search className="size-3.5" />
              <span className="flex-1">Search</span>
              <Kbd className="h-5 border-0 bg-background/70 px-1.5">⌘K</Kbd>
            </button>
            <button
              type="button"
              onClick={() => setSearchOpen(true)}
              className="inline-flex size-9 items-center justify-center rounded-lg text-muted-foreground hover:bg-accent hover:text-foreground md:hidden"
              aria-label="Search documentation"
            >
              <Search className="size-[18px]" />
            </button>
            <ThemeToggle />
            <a
              href="https://github.com/adzamkomladev/Inertia.Net"
              target="_blank"
              rel="noreferrer"
              aria-label="Inertia.Net on GitHub"
              className="inline-flex size-9 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            >
              <GithubIcon className="size-[18px]" />
            </a>
          </div>
        </div>
      </header>
      <SearchDialog open={searchOpen} onOpenChange={setSearchOpen} />
    </>
  )
}
