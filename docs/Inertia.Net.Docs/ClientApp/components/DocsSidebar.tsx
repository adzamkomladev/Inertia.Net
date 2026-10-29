import { Link, usePage } from '@inertiajs/react'
import { navigation } from '../lib/content'
import { cn } from '../lib/utils'

export function SidebarNav({ onNavigate }: { onNavigate?: () => void }) {
  const { url } = usePage()
  const path = url.split('?')[0].split('#')[0]

  return (
    <nav className="space-y-7 pb-10 text-sm">
      {navigation.map((section) => (
        <div key={section.title}>
          <p className="mb-2 px-3 font-medium text-foreground">{section.title}</p>
          <ul className="border-l border-border">
            {section.pages.map((page) => {
              const active = path === page.url
              return (
                <li key={page.slug}>
                  <Link
                    href={page.url}
                    onClick={onNavigate}
                    aria-current={active ? 'page' : undefined}
                    className={cn(
                      '-ml-px block border-l py-1.5 pl-3 leading-5 transition-colors',
                      active
                        ? 'border-primary font-medium text-primary'
                        : 'border-transparent text-muted-foreground hover:border-muted-foreground/50 hover:text-foreground',
                    )}
                  >
                    {page.title}
                  </Link>
                </li>
              )
            })}
          </ul>
        </div>
      ))}
    </nav>
  )
}

export function DocsSidebar() {
  return (
    <aside className="hidden w-56 shrink-0 lg:block">
      <div className="scrollbar-thin sticky top-16 -ml-3 h-[calc(100vh-4rem)] overflow-y-auto px-3 pt-8">
        <SidebarNav />
      </div>
    </aside>
  )
}
