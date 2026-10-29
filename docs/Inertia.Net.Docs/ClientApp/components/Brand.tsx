import { Link } from '@inertiajs/react'
import { Orbit } from 'lucide-react'
import { cn } from '../lib/utils'

export function Brand({ className, compact = false }: { className?: string; compact?: boolean }) {
  return (
    <Link href="/" className={cn('group flex items-center gap-2.5 font-semibold tracking-tight', className)}>
      <span className="grid size-7 place-items-center rounded-md bg-primary text-primary-foreground">
        <Orbit className="size-4" strokeWidth={2.2} />
      </span>
      {!compact && (
        <span className="text-[15px]">
          Inertia<span className="text-muted-foreground">.Net</span>
        </span>
      )}
    </Link>
  )
}
