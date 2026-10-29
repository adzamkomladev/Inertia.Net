import { Link, usePage } from '@inertiajs/react'
import { Menu, Search } from 'lucide-react'
import { useState } from 'react'
import { Brand } from './Brand'
import { GithubIcon } from './icons/GithubIcon'
import { SearchDialog } from './SearchDialog'
import { ThemeToggle } from './ThemeToggle'
import { Kbd } from './ui/kbd'

export function SiteHeader({ onOpenNav }: { onOpenNav?: () => void }) {
  const { props } = usePage()
  const [searchOpen, setSearchOpen] = useState(false)

  return (
    <>
      <header className="sticky top-0 z-40 border-b border-border/70 bg-background/80 backdrop-blur-xl">
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
          <span className="hidden h-5 w-px bg-border sm:block" />
          <nav className="hidden items-center gap-1 text-sm sm:flex">
            <Link href="/docs/introduction" className="rounded-md px-2.5 py-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">
              Documentation
            </Link>
            <a
              href="https://github.com/adzamkomladev/Inertia.Net-Samples"
              target="_blank"
              rel="noreferrer"
              className="rounded-md px-2.5 py-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            >
              Examples
            </a>
          </nav>

          <div className="ml-auto flex items-center gap-1.5">
            <button
              type="button"
              onClick={() => setSearchOpen(true)}
              className="hidden h-9 w-56 items-center gap-2 rounded-lg border border-border bg-muted/40 px-3 text-left text-sm text-muted-foreground transition-colors hover:bg-accent md:flex xl:w-64"
            >
              <Search className="size-3.5" />
              <span className="flex-1">Search docs...</span>
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
            <a
              href="https://www.nuget.org/packages/Inertia.Net"
              target="_blank"
              rel="noreferrer"
              className="ml-1 hidden items-center rounded-full border border-border bg-muted/50 px-2.5 py-1 font-mono text-[11px] text-muted-foreground transition-colors hover:text-foreground sm:flex"
            >
              v{String(props.version)}
            </a>
          </div>
        </div>
      </header>
      <SearchDialog open={searchOpen} onOpenChange={setSearchOpen} />
    </>
  )
}
