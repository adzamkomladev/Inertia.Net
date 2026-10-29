import { ChevronDown } from 'lucide-react'
import type { ReactNode } from 'react'

export function Accordion({ children }: { children: ReactNode }) {
  return <div className="not-prose my-6 divide-y divide-border overflow-hidden rounded-xl border border-border bg-card">{children}</div>
}

export function AccordionItem({ title, children }: { title: string; children: ReactNode }) {
  return (
    <details className="group px-4 py-3 [&_summary::-webkit-details-marker]:hidden">
      <summary className="flex cursor-pointer list-none items-center justify-between gap-4 text-sm font-medium">
        {title}
        <ChevronDown className="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" />
      </summary>
      <div className="pt-3 text-sm leading-6 text-muted-foreground [&>p]:m-0 [&>p+p]:mt-3 [&_code]:rounded-md [&_code]:border [&_code]:border-border [&_code]:bg-muted [&_code]:px-1 [&_code]:py-0.5 [&_code]:font-mono [&_code]:text-[0.85em]">
        {children}
      </div>
    </details>
  )
}
