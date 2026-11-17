import { Link } from '@inertiajs/react'
import { Link2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { AnchorHTMLAttributes, ComponentPropsWithoutRef, ReactNode } from 'react'
import { Accordion, AccordionItem } from './Accordion'
import { Badge } from './ui/badge'
import { Callout } from './Callout'
import { Cards, Card } from './Cards'
import { CodeBlock } from './CodeBlock'
import { Kbd } from './ui/kbd'
import { Steps, Step } from './Steps'
import { Tabs, Tab } from './Tabs'
import { BrandMark, brandOf } from './Brands'
import { Bars, Contenders, Flow, ScrollArea, Sequence } from './Visuals'

function MdxLink({ href = '', children }: AnchorHTMLAttributes<HTMLAnchorElement>) {
  // Only docs pages are Inertia routes; files like /llms.txt need a normal navigation.
  if (href.startsWith('/docs/') || href === '/') {
    return <Link href={href}>{children}</Link>
  }

  const external = /^https?:\/\//.test(href)
  return (
    <a href={href} {...(external ? { target: '_blank', rel: 'noreferrer' } : {})}>
      {children}
    </a>
  )
}

function MdxHeading({ as: Tag, id, children }: { as: 'h2' | 'h3' | 'h4'; id?: string; children?: ReactNode }) {
  return (
    <Tag id={id} className="group">
      {children}
      {id && (
        <a
          href={`#${id}`}
          aria-label="Link to this section"
          className="ml-2 inline-flex -translate-x-1 align-middle text-muted-foreground opacity-0 transition-[opacity,transform,color] duration-200 ease-[var(--ease-out)] hover:text-primary focus-visible:translate-x-0 focus-visible:opacity-100 group-hover:translate-x-0 group-hover:opacity-100"
        >
          <Link2 className="size-[0.7em]" />
        </a>
      )}
    </Tag>
  )
}

function MdxTable({ children }: { children: ReactNode }) {
  const ref = useRef<HTMLTableElement>(null)
  const [stack, setStack] = useState(false)
  // Wide tables turn into one card per row on phones, with each cell labelled by its column header.
  useEffect(() => {
    const t = ref.current
    if (!t) return
    const heads = [...t.querySelectorAll('thead th')].map((th) => th.textContent?.trim() ?? '')
    t.querySelectorAll('tbody tr').forEach((tr) => [...tr.children].forEach((td, i) => td.setAttribute('data-label', heads[i] ?? '')))
    const overflows = t.scrollWidth > (t.parentElement?.clientWidth ?? 0) + 1
    setStack(heads.length >= 4 || (window.innerWidth < 640 && overflows))
  }, [children])

  return (
    <div data-stack={stack || undefined} className="not-prose my-7 overflow-hidden rounded-xl border border-border bg-card">
      <ScrollArea className="no-scrollbar overflow-x-auto">
      <table ref={ref} className="w-full border-collapse text-left text-[13px] leading-6 sm:text-sm tabular-nums [&_code]:rounded [&_code]:border [&_code]:border-border [&_code]:bg-muted [&_code]:px-1.5 [&_code]:py-0.5 [&_code]:font-mono [&_code]:[overflow-wrap:anywhere] [&_code]:[box-decoration-break:clone] [&_code]:[-webkit-box-decoration-break:clone] sm:[&_code]:whitespace-nowrap sm:[&_td:first-child]:min-w-36 [&_code]:text-[0.8em] [&_code]:font-medium [&_code]:[font-variant-ligatures:none] [&_a]:font-medium [&_a]:text-primary [&_a]:underline [&_a]:decoration-primary/35 [&_a]:underline-offset-4 [&_thead]:bg-[var(--primary-soft)] [&_td:first-child]:font-medium [&_td:first-child]:text-foreground [&_th]:text-primary [&_th]:border-b [&_th]:border-border [&_th]:px-3 [&_th]:py-2.5 sm:[&_th]:px-4 [&_th]:text-xs [&_th]:font-semibold [&_td]:border-b [&_td]:border-border/70 [&_td]:px-3 [&_td]:py-3 sm:[&_td]:px-4 [&_td]:align-top [&_td]:text-[color-mix(in_oklab,var(--foreground)_88%,var(--background))] [&_td[align=right]]:whitespace-nowrap [&_td[style*=right]]:whitespace-nowrap [&_th[align=right]]:min-w-24 [&_th[style*=right]]:min-w-24 [&_tbody_tr]:transition-colors [&_tbody_tr]:duration-150 [&_tbody_tr:hover]:bg-muted/40 [&_tr:last-child_td]:border-0">
        {children}
      </table>
      </ScrollArea>
    </div>
  )
}

// A cell that names a compared framework gets its mark.
function MdxCell({ children, ...rest }: ComponentPropsWithoutRef<'td'>) {
  const brand = typeof children === 'string' ? brandOf(children) : undefined
  return (
    <td {...rest}>
      {brand ? (
        <span className="inline-flex items-center gap-2.5">
          <BrandMark brand={brand} className="size-5" />
          {children}
        </span>
      ) : (
        children
      )}
    </td>
  )
}

export const mdxComponents = {
  td: MdxCell,
  a: MdxLink,
  h2: (props: ComponentPropsWithoutRef<'h2'>) => <MdxHeading as="h2" {...props} />,
  h3: (props: ComponentPropsWithoutRef<'h3'>) => <MdxHeading as="h3" {...props} />,
  h4: (props: ComponentPropsWithoutRef<'h4'>) => <MdxHeading as="h4" {...props} />,
  pre: CodeBlock,
  table: MdxTable,
  Callout,
  Cards,
  Card,
  Tabs,
  Tab,
  Steps,
  Step,
  Accordion,
  AccordionItem,
  Badge,
  Kbd,
  Bars,
  Contenders,
  Flow,
  Sequence,
}
