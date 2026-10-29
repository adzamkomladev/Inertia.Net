import { Children, isValidElement, useState, type ReactElement, type ReactNode } from 'react'
import { cn } from '../lib/utils'

interface TabProps {
  label: string
  children: ReactNode
}

export function Tabs({ children, defaultValue }: { children: ReactNode; defaultValue?: string }) {
  const tabs = Children.toArray(children).filter((child): child is ReactElement<TabProps> => isValidElement(child)) as ReactElement<TabProps>[]
  const [active, setActive] = useState(defaultValue ?? tabs[0]?.props.label)
  const current = tabs.find((tab) => tab.props.label === active) ?? tabs[0]

  return (
    <div className="my-6 overflow-hidden rounded-xl border border-border bg-card">
      <div role="tablist" className="not-prose flex items-center gap-1 overflow-x-auto border-b border-border bg-muted/30 px-2 py-1.5">
        {tabs.map((tab) => {
          const selected = tab.props.label === current?.props.label
          return (
            <button
              key={tab.props.label}
              type="button"
              role="tab"
              aria-selected={selected}
              onClick={() => setActive(tab.props.label)}
              className={cn(
                'shrink-0 rounded-md px-2.5 py-1.5 text-xs font-medium transition-colors',
                selected ? 'bg-background text-foreground shadow-sm ring-1 ring-border' : 'text-muted-foreground hover:text-foreground',
              )}
            >
              {tab.props.label}
            </button>
          )
        })}
      </div>
      <div
        role="tabpanel"
        className="[&>figure]:my-0 [&>figure]:rounded-none [&>figure]:border-0 [&>figure:not(:first-child)]:border-t [&>figure:not(:first-child)]:border-border [&>p]:m-0 [&>p]:px-4 [&>p]:py-3 [&>p]:text-sm [&>p]:leading-6 [&>p]:text-muted-foreground"
      >
        {current?.props.children}
      </div>
    </div>
  )
}

export function Tab(_props: TabProps) {
  return null
}
