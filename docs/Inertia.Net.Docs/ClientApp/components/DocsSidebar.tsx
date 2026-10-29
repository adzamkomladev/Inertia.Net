import { Link, usePage } from '@inertiajs/react'
import { navigation } from '../lib/content'
import { cn } from '../lib/utils'

export function SidebarNav({ onNavigate }: { onNavigate?: () => void }) {
  const { url } = usePage()
  const path = url.split('?')[0].split('#')[0]

  return (
    <nav className="space-y-6 pb-10">
      {navigation.map((section) => {
        const groups = new Map<string, typeof section.pages>()
        for (const page of section.pages) {
          const group = page.group ?? ''
          groups.set(group, [...(groups.get(group) ?? []), page])
        }

        return (
          <div key={section.title}>
            <p className="mb-2 px-2.5 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground/80">{section.title}</p>
            {[...groups.entries()].map(([group, pages]) => (
              <div key={group} className="mb-3 last:mb-0">
                {group && <p className="mb-1 mt-2 px-2.5 text-[11px] font-medium text-muted-foreground/70">{group}</p>}
                <ul className="space-y-0.5">
                  {pages.map((page) => {
                    const active = path === page.url
                    return (
                      <li key={page.slug}>
                        <Link
                          href={page.url}
                          onClick={onNavigate}
                          aria-current={active ? 'page' : undefined}
                          className={cn(
                            'block rounded-lg px-2.5 py-[7px] text-[13.5px] leading-5 transition-colors',
                            active
                              ? 'bg-primary/10 font-medium text-primary'
                              : 'text-muted-foreground hover:bg-accent hover:text-foreground',
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
          </div>
        )
      })}
    </nav>
  )
}

export function DocsSidebar() {
  return (
    <aside className="hidden w-64 shrink-0 lg:block">
      <div className="scrollbar-thin sticky top-16 -ml-3 h-[calc(100vh-4rem)] overflow-y-auto px-3 pt-8">
        <SidebarNav />
      </div>
    </aside>
  )
}
