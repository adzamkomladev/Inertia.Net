import { Link } from '@inertiajs/react'
import { Orbit } from 'lucide-react'
import { cn } from '../lib/utils'

export function Brand({ className, compact = false }: { className?: string; compact?: boolean }) {
  return (
    <Link href="/" className={cn('group flex items-center gap-2.5 rounded-md font-semibold tracking-tight', className)}>
      <span className="grid size-7 place-items-center rounded-lg bg-primary text-primary-foreground shadow-[inset_0_1px_0_oklch(1_0_0/0.25),0_1px_2px_oklch(0.3_0.15_285/0.4)] transition-transform duration-300 ease-[var(--ease-out)] group-hover:rotate-[24deg]">
        <Orbit className="size-4" strokeWidth={2.2} />
      </span>
      {!compact && (
        <span className="text-[15px]">
          Inertia<span className="text-muted-foreground transition-colors duration-200 group-hover:text-primary">.Net</span>
        </span>
      )}
    </Link>
  )
}
