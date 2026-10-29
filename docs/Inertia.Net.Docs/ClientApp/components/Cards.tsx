import { Link } from '@inertiajs/react'
import { ArrowRight, BookOpen, Boxes, Code2, Gauge, Rocket, Server, ShieldCheck, Sparkles, SquareTerminal } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '../lib/utils'

const icons = {
  book: BookOpen,
  blocks: Boxes,
  code: Code2,
  rocket: Rocket,
  server: Server,
  shield: ShieldCheck,
  sparkles: Sparkles,
  gauge: Gauge,
  terminal: SquareTerminal,
} as const

export type CardIcon = keyof typeof icons

export function Cards({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('not-prose my-6 grid gap-3 sm:grid-cols-2', className)}>{children}</div>
}

export function Card({ title, href, icon, children }: { title: string; href?: string; icon?: CardIcon; children?: ReactNode }) {
  const Icon = icon ? icons[icon] : undefined
  const body = (
    <>
      <div className="flex items-start gap-3">
        {Icon && (
          <span className="grid size-9 shrink-0 place-items-center rounded-lg border border-border bg-muted/60 text-primary">
            <Icon className="size-[18px]" />
          </span>
        )}
        <div className="min-w-0 flex-1">
          <p className="font-semibold leading-6 tracking-tight">{title}</p>
          {children && <div className="mt-1 text-[13px] leading-5 text-muted-foreground [&>p]:m-0">{children}</div>}
        </div>
        {href && <ArrowRight className="mt-1 size-4 shrink-0 text-muted-foreground/40 transition-transform group-hover:translate-x-0.5 group-hover:text-primary" />}
      </div>
    </>
  )

  const className = 'group rounded-xl border border-border bg-card p-4 transition-colors hover:border-primary/35 hover:bg-accent/40'

  if (href?.startsWith('/')) {
    return (
      <Link href={href} className={className}>
        {body}
      </Link>
    )
  }

  if (href) {
    return (
      <a href={href} target="_blank" rel="noreferrer" className={className}>
        {body}
      </a>
    )
  }

  return <div className={className}>{body}</div>
}
