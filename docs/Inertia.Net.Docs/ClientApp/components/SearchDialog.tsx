import * as Dialog from '@radix-ui/react-dialog'
import { router } from '@inertiajs/react'
import { Command } from 'cmdk'
import { ArrowRight, FileText, Search, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { getSearchIndex, type SearchEntry } from '../lib/content'
import { Kbd } from './ui/kbd'

function score(entry: SearchEntry, query: string) {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean)
  if (terms.length === 0) return 1

  const title = entry.title.toLowerCase()
  const description = entry.description.toLowerCase()
  const headings = entry.headings.map((heading) => heading.toLowerCase())
  const body = entry.body.toLowerCase()

  if (!terms.every((term) => [title, description, ...headings, body].some((value) => value.includes(term)))) {
    return 0
  }

  return terms.reduce((total, term) => {
    if (title === term) return total + 120
    if (title.startsWith(term)) return total + 80
    if (title.includes(term)) return total + 55
    if (headings.some((heading) => heading === term)) return total + 40
    if (headings.some((heading) => heading.includes(term))) return total + 28
    if (description.includes(term)) return total + 18
    if (body.includes(term)) return total + 6
    return total
  }, 0)
}

export function SearchDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [entries, setEntries] = useState<SearchEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [query, setQuery] = useState('')

  useEffect(() => {
    if (!open) return
    setLoading(true)
    getSearchIndex()
      .then(setEntries)
      .finally(() => setLoading(false))
  }, [open])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
        event.preventDefault()
        onOpenChange(!open)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [open, onOpenChange])

  const select = (url: string) => {
    onOpenChange(false)
    setQuery('')
    router.visit(url)
  }

  useEffect(() => {
    if (!open) setQuery('')
  }, [open])

  const results = useMemo(() => {
    return entries
      .map((entry) => ({ entry, score: score(entry, query) }))
      .filter((result) => result.score > 0)
      .sort((a, b) => b.score - a.score || a.entry.title.localeCompare(b.entry.title))
      .slice(0, 50)
      .map((result) => result.entry)
  }, [entries, query])

  const groups = useMemo(() => {
    const grouped = new Map<string, SearchEntry[]>()
    for (const entry of results) {
      const items = grouped.get(entry.section) ?? []
      items.push(entry)
      grouped.set(entry.section, items)
    }
    return [...grouped.entries()]
  }, [results])

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/45 backdrop-blur-[2px] animate-in" />
        <Dialog.Content
          aria-describedby={undefined}
          className="fixed left-1/2 top-[12vh] z-50 w-[calc(100%-2rem)] max-w-2xl -translate-x-1/2 overflow-hidden rounded-2xl border border-border bg-popover text-popover-foreground shadow-2xl zoom-in-95"
        >
          <Dialog.Title className="sr-only">Search documentation</Dialog.Title>
          <Command shouldFilter={false} className="flex max-h-[min(70vh,560px)] flex-col">
            <div className="flex items-center gap-3 border-b border-border px-4">
              <Search className="size-4 shrink-0 text-muted-foreground" />
              <Command.Input
                value={query}
                onValueChange={setQuery}
                autoFocus
                placeholder="Search the documentation..."
                className="h-12 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
              />
              <button
                type="button"
                onClick={() => onOpenChange(false)}
                className="rounded-md p-1 text-muted-foreground hover:bg-accent hover:text-foreground"
                aria-label="Close search"
              >
                <X className="size-4" />
              </button>
            </div>
            <Command.List className="scrollbar-thin min-h-0 flex-1 overflow-y-auto overscroll-contain p-2">
              {loading && entries.length === 0 && <Command.Loading className="px-3 py-8 text-center text-sm text-muted-foreground">Loading index...</Command.Loading>}
              <Command.Empty className="px-3 py-10 text-center text-sm text-muted-foreground">
                No results for <span className="font-medium text-foreground">“{query}”</span>
              </Command.Empty>
              {groups.map(([section, items]) => (
                <Command.Group
                  key={section}
                  heading={section}
                  className="mb-2 [&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1.5 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:tracking-wider [&_[cmdk-group-heading]]:text-muted-foreground"
                >
                  {items.map((entry) => (
                    <Command.Item
                      key={entry.id}
                      value={`${entry.title} ${entry.description} ${entry.headings.join(' ')} ${entry.body}`}
                      onSelect={() => select(entry.url)}
                      className="group flex cursor-pointer items-center gap-3 rounded-lg px-2.5 py-2.5 text-sm outline-none data-[selected=true]:bg-accent data-[selected=true]:text-accent-foreground"
                    >
                      <span className="grid size-8 shrink-0 place-items-center rounded-md border border-border bg-muted/60 text-muted-foreground group-data-[selected=true]:border-primary/20 group-data-[selected=true]:bg-primary/10 group-data-[selected=true]:text-primary">
                        <FileText className="size-4" />
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">{entry.title}</span>
                        {entry.description && <span className="block truncate text-xs text-muted-foreground">{entry.description}</span>}
                      </span>
                      <ArrowRight className="size-3.5 shrink-0 text-muted-foreground/50 opacity-0 transition-opacity group-data-[selected=true]:opacity-100" />
                    </Command.Item>
                  ))}
                </Command.Group>
              ))}
            </Command.List>
            <div className="flex items-center justify-between gap-4 border-t border-border px-4 py-2.5 text-[11px] text-muted-foreground">
              <span className="flex items-center gap-1.5">
                <Kbd>↑</Kbd>
                <Kbd>↓</Kbd>
                to navigate
              </span>
              <span className="flex items-center gap-1.5">
                <Kbd>↵</Kbd>
                to open
              </span>
              <span className="flex items-center gap-1.5">
                <Kbd>esc</Kbd>
                to close
              </span>
            </div>
          </Command>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
