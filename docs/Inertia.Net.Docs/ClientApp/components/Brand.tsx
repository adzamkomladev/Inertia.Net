import { Link } from '@inertiajs/react'
import { Orbit } from 'lucide-react'
import { cn } from '../lib/utils'

export function Brand({ className, compact = false }: { className?: string; compact?: boolean }) {
  return (
    <Link href="/" className={cn('group flex items-center gap-2.5 font-semibold tracking-tight', className)}>
      <span className="relative grid size-8 place-items-center overflow-hidden rounded-lg bg-gradient-to-br from-violet-500 via-indigo-500 to-blue-500 text-white shadow-sm shadow-indigo-500/25 ring-1 ring-white/20">
        <Orbit className="size-[18px]" strokeWidth={2.2} />
      </span>
      {!compact && (
        <span className="text-[15px]">
          Inertia<span className="text-muted-foreground">.Net</span>
        </span>
      )}
    </Link>
  )
}
