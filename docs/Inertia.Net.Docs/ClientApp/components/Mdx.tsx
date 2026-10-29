import { Link } from '@inertiajs/react'
import { Link2 } from 'lucide-react'
import type { AnchorHTMLAttributes, ComponentPropsWithoutRef, ReactNode } from 'react'
import { Accordion, AccordionItem } from './Accordion'
import { Badge } from './ui/badge'
import { Callout } from './Callout'
import { Cards, Card } from './Cards'
import { CodeBlock } from './CodeBlock'
import { Kbd } from './ui/kbd'
import { Steps, Step } from './Steps'
import { Tabs, Tab } from './Tabs'

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
          className="ml-2 inline-flex align-middle text-muted-foreground/0 transition-colors group-hover:text-muted-foreground hover:!text-primary"
        >
          <Link2 className="size-[0.7em]" />
        </a>
      )}
    </Tag>
  )
}

function MdxTable({ children }: { children: ReactNode }) {
  return (
    <div className="not-prose my-6 overflow-x-auto rounded-xl border border-border">
      <table className="w-full border-collapse text-left text-sm [&_thead]:bg-muted/50 [&_th]:whitespace-nowrap [&_th]:border-b [&_th]:border-border [&_th]:px-3.5 [&_th]:py-2.5 [&_th]:font-semibold [&_td]:border-b [&_td]:border-border/70 [&_td]:px-3.5 [&_td]:py-2.5 [&_td]:align-top [&_tr:last-child_td]:border-0">
        {children}
      </table>
    </div>
  )
}

export const mdxComponents = {
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
}
