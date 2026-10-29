import { AlertTriangle, CircleCheck, Info, Lightbulb, OctagonAlert } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '../lib/utils'

const types = {
  info: {
    icon: Info,
    label: 'Note',
    className: 'border-sky-500/25 bg-sky-500/[0.06] text-sky-950 dark:text-sky-100 [&_svg]:text-sky-600 dark:[&_svg]:text-sky-400',
  },
  tip: {
    icon: Lightbulb,
    label: 'Tip',
    className: 'border-emerald-500/25 bg-emerald-500/[0.06] text-emerald-950 dark:text-emerald-100 [&_svg]:text-emerald-600 dark:[&_svg]:text-emerald-400',
  },
  warning: {
    icon: AlertTriangle,
    label: 'Warning',
    className: 'border-amber-500/30 bg-amber-500/[0.07] text-amber-950 dark:text-amber-100 [&_svg]:text-amber-600 dark:[&_svg]:text-amber-400',
  },
  danger: {
    icon: OctagonAlert,
    label: 'Important',
    className: 'border-red-500/25 bg-red-500/[0.06] text-red-950 dark:text-red-100 [&_svg]:text-red-600 dark:[&_svg]:text-red-400',
  },
  success: {
    icon: CircleCheck,
    label: 'Success',
    className: 'border-emerald-500/25 bg-emerald-500/[0.06] text-emerald-950 dark:text-emerald-100 [&_svg]:text-emerald-600 dark:[&_svg]:text-emerald-400',
  },
} as const

export function Callout({
  type = 'info',
  title,
  children,
}: {
  type?: keyof typeof types
  title?: string
  children: ReactNode
}) {
  const config = types[type] ?? types.info
  const Icon = config.icon

  return (
    <div className={cn('not-prose my-6 flex gap-3 rounded-xl border p-4 text-sm', config.className)}>
      <Icon className="mt-0.5 size-[18px] shrink-0" />
      <div className="min-w-0 flex-1 [&>p]:m-0 [&>p+p]:mt-3 [&_a]:font-medium [&_a]:underline [&_a]:underline-offset-4 [&_code]:rounded-md [&_code]:border [&_code]:border-current/10 [&_code]:bg-current/5 [&_code]:px-1 [&_code]:py-0.5 [&_code]:font-mono [&_code]:text-[0.85em]">
        <p className="mb-1 font-semibold">{title ?? config.label}</p>
        {children}
      </div>
    </div>
  )
}
