import dotnet from '../assets/logos/dotnet.svg'
import laravel from '../assets/logos/laravel.svg'
import nextjs from '../assets/logos/nextjs.svg'
import nuxt from '../assets/logos/nuxt.svg'
import tanstack from '../assets/logos/tanstack.svg'

// Marks: .NET and Next.js from dashboardicons.com, Laravel from simple-icons, Nuxt from nuxt.com, TanStack from tanstack.com/images/brand.
export type Brand = { name: string; logo: string; bar: string; pad: string }

export const brands: Brand[] = [
  { name: 'Inertia.Net', logo: dotnet, bar: '#512bd4', pad: 'p-[3px]' },
  { name: 'Next.js', logo: nextjs, bar: 'var(--foreground)', pad: 'p-[3px]' },
  { name: 'TanStack Start', logo: tanstack, bar: '#f59e0b', pad: 'p-[4px]' },
  { name: 'Nuxt', logo: nuxt, bar: '#00dc82', pad: 'p-[4px]' },
  { name: 'Laravel', logo: laravel, bar: '#ff2d20', pad: 'p-[4px]' },
]

/** Matches "Nuxt", "Inertia.Net, JIT" and so on; returns undefined for anything else. */
export function brandOf(label: string): Brand | undefined {
  return brands.find((b) => label === b.name || label.startsWith(b.name + ','))
}

/** The logo on a white tile, so dark marks stay readable in dark mode. */
export function BrandMark({ brand, className = 'size-6' }: { brand: Brand; className?: string }) {
  return (
    <span className={`inline-grid shrink-0 place-items-center rounded-md bg-white ring-1 ring-black/10 ${brand.pad} ${className}`}>
      <img src={brand.logo} alt="" className="size-full object-contain" draggable={false} />
    </span>
  )
}
