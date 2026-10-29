import type { ReactNode } from 'react'

export function Steps({ children }: { children: ReactNode }) {
  return (
    <div className="not-prose my-6 [counter-reset:step] [&>ol]:m-0 [&>ol]:list-none [&>ol]:space-y-5 [&>ol]:p-0">
      <ol>{children}</ol>
    </div>
  )
}

export function Step({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <li className="relative pl-10 [counter-increment:step] before:absolute before:left-0 before:top-0 before:grid before:size-7 before:place-items-center before:rounded-full before:border before:border-border before:bg-card before:font-mono before:text-xs before:font-medium before:text-muted-foreground before:content-[counter(step)]">
      {title && <p className="mb-1.5 pt-0.5 font-semibold leading-6">{title}</p>}
      <div className="text-sm leading-6 text-muted-foreground [&>p]:m-0 [&>p+p]:mt-3 [&_code]:rounded-md [&_code]:border [&_code]:border-border [&_code]:bg-muted [&_code]:px-1 [&_code]:py-0.5 [&_code]:font-mono [&_code]:text-[0.85em]">
        {children}
      </div>
    </li>
  )
}
