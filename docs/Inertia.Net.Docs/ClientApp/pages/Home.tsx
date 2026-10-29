import { Link } from '@inertiajs/react'
import {
  ArrowRight,
  Boxes,
  Check,
  Code2,
  ExternalLink,
  Gauge,
  Layers,
  Package,
  Rocket,
  Server,
  ShieldCheck,
  Sparkles,
  Zap,
} from 'lucide-react'
import { useState } from 'react'
import { GithubIcon } from '../components/icons/GithubIcon'
import { InstallCommand } from '../components/InstallCommand'
import { Seo } from '../components/Seo'
import { SiteHeader } from '../components/SiteHeader'
import { Badge } from '../components/ui/badge'
import { cn } from '../lib/utils'

const features = [
  {
    icon: Server,
    title: 'Minimal APIs, MVC or FastEndpoints',
    body: 'One adapter, three backend styles. Render pages from endpoints or controllers without changing how your app routes requests.',
  },
  {
    icon: Boxes,
    title: 'Props that do real work',
    body: 'Lazy, optional, deferred, merged, once, cached and infinite-scroll props. Resolved in a single pass and serialized without reflection.',
  },
  {
    icon: Rocket,
    title: 'Server-side rendering',
    body: 'Render the first visit on Node, start and supervise the process from .NET, and fall back to the client when a render fails.',
  },
  {
    icon: Zap,
    title: 'Vite, built in',
    body: 'Manifest tags, a dev-server hot file, chunk prefetching, CSP nonces and a CDN asset base URL. No custom build plumbing.',
  },
  {
    icon: ShieldCheck,
    title: 'Validate the Inertia way',
    body: 'Validation failures redirect back with errors, bags and Precognition live validation. CSRF protection is one middleware call.',
  },
  {
    icon: Gauge,
    title: 'Fast and AOT-ready',
    body: 'UTF-8 output, pooled buffers, source-generated JSON and no reflection over your props. Native AOT is covered by CI.',
  },
]

const backends = ['Minimal API', 'MVC controllers', 'FastEndpoints']
const frontends = [
  { name: 'React', version: 'React 19', accent: 'from-sky-400 to-cyan-300' },
  { name: 'Vue', version: 'Vue 3', accent: 'from-emerald-400 to-teal-300' },
  { name: 'Svelte', version: 'Svelte 5', accent: 'from-orange-400 to-amber-300' },
]

const serverCode = `app.MapGet("/", () => Render("Home", new InertiaProps
{
    ["greeting"] = "Hello from .NET",
}));`

const clientCode = `export default function Home({ greeting }: { greeting: string }) {
  return <h1>{greeting}</h1>
}`

function CodePreview() {
  const [tab, setTab] = useState<'server' | 'client'>('server')

  return (
    <div className="relative">
      <div className="absolute -inset-4 -z-10 rounded-[2rem] bg-gradient-to-br from-primary/15 via-transparent to-blue-500/10 blur-2xl" />
      <div className="overflow-hidden rounded-2xl border border-border bg-card shadow-2xl shadow-black/5 dark:shadow-black/30">
        <div className="flex items-center gap-2 border-b border-border bg-muted/40 px-4 py-2.5">
          <span className="size-2.5 rounded-full bg-red-400/80" />
          <span className="size-2.5 rounded-full bg-amber-400/80" />
          <span className="size-2.5 rounded-full bg-emerald-400/80" />
          <div className="ml-3 flex items-center gap-1 rounded-md bg-background/70 p-0.5 text-[11px]">
            {(['server', 'client'] as const).map((value) => (
              <button
                key={value}
                type="button"
                onClick={() => setTab(value)}
                className={cn(
                  'rounded px-2 py-1 font-mono transition-colors',
                  tab === value ? 'bg-background text-foreground shadow-sm ring-1 ring-border' : 'text-muted-foreground hover:text-foreground',
                )}
              >
                {value === 'server' ? 'Program.cs' : 'Home.tsx'}
              </button>
            ))}
          </div>
        </div>
        <pre className="min-h-[190px] overflow-x-auto p-5 font-mono text-[12.5px] leading-6 text-muted-foreground">
          <code>
            {tab === 'server' ? (
              <>
                <span className="text-violet-600 dark:text-violet-400">app</span>
                <span>.</span>
                <span className="text-sky-600 dark:text-sky-400">MapGet</span>
                <span>(</span>
                <span className="text-emerald-600 dark:text-emerald-400">"/"</span>
                <span>, () =&gt; </span>
                <span className="text-sky-600 dark:text-sky-400">Render</span>
                <span>(</span>
                <span className="text-emerald-600 dark:text-emerald-400">"Home"</span>
                <span>, </span>
                <span className="text-violet-600 dark:text-violet-400">new</span>
                <span> </span>
                <span className="text-amber-600 dark:text-amber-400">InertiaProps</span>
                <span>{'\n{\n    ['}</span>
                <span className="text-emerald-600 dark:text-emerald-400">"greeting"</span>
                <span>] = </span>
                <span className="text-emerald-600 dark:text-emerald-400">"Hello from .NET"</span>
                <span>,{'\n});'}</span>
              </>
            ) : (
              <>
                <span className="text-violet-600 dark:text-violet-400">export default function</span>
                <span> </span>
                <span className="text-sky-600 dark:text-sky-400">Home</span>
                <span>({'{ greeting }: { greeting: '}</span>
                <span className="text-amber-600 dark:text-amber-400">string</span>
                <span>{' } }) {\n  return <'}</span>
                <span className="text-sky-600 dark:text-sky-400">h1</span>
                <span>&gt;{'{greeting}'}&lt;/</span>
                <span className="text-sky-600 dark:text-sky-400">h1</span>
                <span>&gt;{'\n}'}</span>
              </>
            )}
          </code>
        </pre>
        <div className="flex items-center justify-between gap-3 border-t border-border bg-muted/30 px-4 py-2.5 text-[11px] text-muted-foreground">
          <span className="flex items-center gap-1.5">
            <span className="size-1.5 rounded-full bg-emerald-500" />
            {tab === 'server' ? 'Minimal API endpoint' : 'React page component'}
          </span>
          <span className="font-mono">Inertia.js v3</span>
        </div>
      </div>
    </div>
  )
}

function Footer() {
  return (
    <footer className="border-t border-border bg-muted/20">
      <div className="mx-auto grid max-w-[1200px] gap-10 px-6 py-12 sm:grid-cols-2 lg:grid-cols-4">
        <div className="sm:col-span-2">
          <p className="font-semibold tracking-tight">
            Inertia<span className="text-muted-foreground">.Net</span>
          </p>
          <p className="mt-3 max-w-sm text-sm leading-6 text-muted-foreground">
            A .NET 10 server adapter for Inertia.js v3. Build React, Vue and Svelte apps with ASP.NET Core.
          </p>
          <p className="mt-4 text-xs text-muted-foreground">MIT licensed. Documentation built with Inertia.Net.</p>
        </div>
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Documentation</p>
          <ul className="mt-3 space-y-2 text-sm text-muted-foreground">
            <li><Link href="/docs/introduction" className="hover:text-foreground">Introduction</Link></li>
            <li><Link href="/docs/quick-start" className="hover:text-foreground">Quick start</Link></li>
            <li><Link href="/docs/server-side-rendering" className="hover:text-foreground">Server-side rendering</Link></li>
            <li><Link href="/docs/configuration" className="hover:text-foreground">Configuration</Link></li>
          </ul>
        </div>
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Project</p>
          <ul className="mt-3 space-y-2 text-sm text-muted-foreground">
            <li><a href="https://github.com/adzamkomladev/Inertia.Net" target="_blank" rel="noreferrer" className="hover:text-foreground">GitHub</a></li>
            <li><a href="https://www.nuget.org/packages/Inertia.Net" target="_blank" rel="noreferrer" className="hover:text-foreground">NuGet</a></li>
            <li><a href="https://github.com/adzamkomladev/Inertia.Net-Samples" target="_blank" rel="noreferrer" className="hover:text-foreground">Samples</a></li>
            <li><a href="https://github.com/adzamkomladev/Inertia.Net/blob/main/LICENSE" target="_blank" rel="noreferrer" className="hover:text-foreground">License</a></li>
          </ul>
        </div>
      </div>
    </footer>
  )
}

export default function Home() {
  return (
    <div className="min-h-screen">
      <Seo
        title="Inertia.Net — Inertia.js for ASP.NET Core"
        description="Inertia.Net is a .NET 10 server adapter for Inertia.js v3. Build React, Vue and Svelte apps with ASP.NET Core, without a separate frontend API."
        type="website"
      />
      <SiteHeader />

      <main>
        <section className="relative overflow-hidden border-b border-border">
          <div className="pointer-events-none absolute inset-0 grid-bg opacity-60" />
          <div className="pointer-events-none absolute inset-x-0 top-0 h-[560px] glow" />
          <div className="relative mx-auto max-w-[1200px] px-6 pb-20 pt-16 lg:pb-28 lg:pt-24">
            <div className="grid items-center gap-14 lg:grid-cols-[1.05fr_0.95fr]">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <Badge className="border-primary/20 bg-primary/10 text-primary">
                    <Sparkles className="size-3" />
                    Inertia.js v3 · .NET 10
                  </Badge>
                  <Badge>Native AOT ready</Badge>
                </div>
                <h1 className="mt-6 max-w-2xl text-balance text-4xl font-semibold tracking-[-0.035em] sm:text-5xl lg:text-[3.4rem] lg:leading-[1.06]">
                  The missing .NET adapter for{' '}
                  <span className="bg-gradient-to-r from-violet-500 via-indigo-500 to-blue-500 bg-clip-text text-transparent">Inertia.js</span>
                </h1>
                <p className="mt-5 max-w-xl text-pretty text-[15px] leading-7 text-muted-foreground sm:text-base">
                  Keep your ASP.NET Core routes, controllers, authentication and validation. Add React, Vue or Svelte pages without a separate API or a client-side router.
                </p>
                <div className="mt-7 flex flex-wrap items-center gap-3">
                  <Link
                    href="/docs/introduction"
                    className="inline-flex h-11 items-center gap-2 rounded-xl bg-primary px-5 text-sm font-medium text-primary-foreground shadow-lg shadow-primary/20 transition-colors hover:bg-primary/90"
                  >
                    Get started
                    <ArrowRight className="size-4" />
                  </Link>
                  <Link
                    href="/docs/quick-start"
                    className="inline-flex h-11 items-center gap-2 rounded-xl border border-border bg-background/70 px-5 text-sm font-medium backdrop-blur transition-colors hover:bg-accent"
                  >
                    <Code2 className="size-4" />
                    Quick start
                  </Link>
                  <a
                    href="https://github.com/adzamkomladev/Inertia.Net"
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex h-11 items-center gap-2 rounded-xl px-3 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground"
                  >
                    <GithubIcon className="size-4" />
                    Star on GitHub
                  </a>
                </div>
                <div className="mt-7 flex flex-col gap-2 sm:flex-row sm:items-center">
                  <InstallCommand command="dotnet add package Inertia.Net" />
                  <span className="text-xs text-muted-foreground">or start from a template</span>
                </div>
              </div>
              <CodePreview />
            </div>
          </div>
        </section>

        <section className="mx-auto max-w-[1200px] px-6 py-20 lg:py-24">
          <div className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-primary">Everything the protocol needs</p>
            <h2 className="mt-3 text-3xl font-semibold tracking-tight">One adapter. The whole Inertia v3 surface.</h2>
            <p className="mt-3 text-[15px] leading-7 text-muted-foreground">
              The adapter implements the protocol, not a subset of it: partial reloads, deferred and merged props, once props, infinite scroll, history state, SSR and DevTools.
            </p>
          </div>
          <div className="mt-10 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {features.map(({ icon: Icon, title, body }) => (
              <div key={title} className="group rounded-2xl border border-border bg-card p-5 transition-colors hover:border-primary/30">
                <span className="grid size-10 place-items-center rounded-xl border border-border bg-muted/60 text-primary transition-colors group-hover:border-primary/20 group-hover:bg-primary/10">
                  <Icon className="size-5" />
                </span>
                <h3 className="mt-4 font-semibold tracking-tight">{title}</h3>
                <p className="mt-1.5 text-[13px] leading-6 text-muted-foreground">{body}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="border-y border-border bg-muted/20">
          <div className="mx-auto grid max-w-[1200px] gap-12 px-6 py-20 lg:grid-cols-2 lg:items-center lg:py-24">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-primary">One server, three frontends</p>
              <h2 className="mt-3 text-3xl font-semibold tracking-tight">Your backend stays the source of truth.</h2>
              <p className="mt-3 max-w-lg text-[15px] leading-7 text-muted-foreground">
                Endpoints return pages, not JSON envelopes. Inertia links and forms keep the SPA feel while ASP.NET Core keeps ownership of routing, auth and validation.
              </p>
              <ul className="mt-7 space-y-3">
                {[
                  'Minimal APIs, MVC controllers and FastEndpoints',
                  'Validation errors redirect back automatically',
                  'CSRF, flash data and encrypted history state',
                  'Vite manifests, dev server and chunk prefetching',
                ].map((item) => (
                  <li key={item} className="flex items-start gap-2.5 text-sm text-muted-foreground">
                    <span className="mt-0.5 grid size-5 shrink-0 place-items-center rounded-full bg-primary/10 text-primary">
                      <Check className="size-3" />
                    </span>
                    {item}
                  </li>
                ))}
              </ul>
              <div className="mt-7 flex flex-wrap gap-2">
                {backends.map((backend) => (
                  <Badge key={backend} className="bg-background">{backend}</Badge>
                ))}
                <Badge className="bg-background">.NET 10</Badge>
              </div>
            </div>
            <div className="grid gap-3 sm:grid-cols-3">
              {frontends.map((frontend) => (
                <div key={frontend.name} className="rounded-2xl border border-border bg-card p-5 text-center">
                  <span className={cn('mx-auto grid size-12 place-items-center rounded-2xl bg-gradient-to-br text-lg font-bold text-white shadow-inner', frontend.accent)}>
                    {frontend.name[0]}
                  </span>
                  <p className="mt-3 font-semibold">{frontend.name}</p>
                  <p className="text-xs text-muted-foreground">{frontend.version}</p>
                </div>
              ))}
              <div className="sm:col-span-3 rounded-2xl border border-dashed border-border p-4 text-center text-xs text-muted-foreground">
                Same server. Same props. Pick the client package for your framework.
              </div>
            </div>
          </div>
        </section>

        <section className="mx-auto max-w-[1200px] px-6 py-20 lg:py-24">
          <div className="grid gap-10 lg:grid-cols-[0.9fr_1.1fr] lg:items-start">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-primary">Production details included</p>
              <h2 className="mt-3 text-3xl font-semibold tracking-tight">The boring parts are already handled.</h2>
              <p className="mt-3 text-[15px] leading-7 text-muted-foreground">
                Asset versioning, SSR circuit breakers, CSRF cookies, proxy headers, Native AOT and generated TypeScript types are part of the package — not a recipe you copy from a blog post.
              </p>
              <Link href="/docs/configuration" className="mt-6 inline-flex items-center gap-1.5 text-sm font-medium text-primary hover:underline">
                Explore the configuration <ArrowRight className="size-3.5" />
              </Link>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              {[
                { icon: ShieldCheck, title: 'Antiforgery', body: 'XSRF cookie, validation and 419 responses' },
                { icon: Layers, title: 'History state', body: 'Encryption, clearing and fragments' },
                { icon: Rocket, title: 'SSR supervision', body: 'Managed Node process, health and breaker' },
                { icon: Package, title: 'Native AOT', body: 'Source-generated JSON, zero reflection' },
              ].map(({ icon: Icon, title, body }) => (
                <div key={title} className="rounded-xl border border-border bg-card p-4">
                  <Icon className="size-5 text-primary" />
                  <p className="mt-3 text-sm font-semibold">{title}</p>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{body}</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section className="border-t border-border">
          <div className="relative mx-auto max-w-[1200px] overflow-hidden px-6 py-20 text-center lg:py-24">
            <div className="pointer-events-none absolute inset-x-0 bottom-0 h-80 glow" />
            <div className="relative">
              <h2 className="text-3xl font-semibold tracking-tight">Ready to build the .NET way?</h2>
              <p className="mx-auto mt-3 max-w-xl text-[15px] leading-7 text-muted-foreground">
                Install the package, add a root template, and render your first page in a few minutes.
              </p>
              <div className="mt-7 flex flex-wrap items-center justify-center gap-3">
                <Link href="/docs/installation" className="inline-flex h-11 items-center gap-2 rounded-xl bg-primary px-5 text-sm font-medium text-primary-foreground shadow-lg shadow-primary/20 transition-colors hover:bg-primary/90">
                  Install Inertia.Net
                  <ArrowRight className="size-4" />
                </Link>
                <a
                  href="https://github.com/adzamkomladev/Inertia.Net-Samples"
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex h-11 items-center gap-2 rounded-xl border border-border bg-background px-5 text-sm font-medium transition-colors hover:bg-accent"
                >
                  Browse samples
                  <ExternalLink className="size-4" />
                </a>
              </div>
            </div>
          </div>
        </section>
      </main>

      <Footer />
    </div>
  )
}
