import { Link } from '@inertiajs/react'
import { ArrowRight } from 'lucide-react'
import { useState, type ComponentType, type SVGProps } from 'react'
import { ReactLogo, SvelteLogo, VueLogo } from '../components/icons/FrameworkLogos'
import { GithubIcon } from '../components/icons/GithubIcon'
import { InstallCommand } from '../components/InstallCommand'
import { Seo } from '../components/Seo'
import { SiteHeader } from '../components/SiteHeader'
import { cn } from '../lib/utils'

const frameworks: { name: string; package: string; Logo: ComponentType<SVGProps<SVGSVGElement>> }[] = [
  { name: 'React', package: '@inertiajs/react', Logo: ReactLogo },
  { name: 'Vue', package: '@inertiajs/vue3', Logo: VueLogo },
  { name: 'Svelte', package: '@inertiajs/svelte', Logo: SvelteLogo },
]

const reasons = [
  {
    title: 'No API layer to build',
    body: 'Your endpoints return a page name and its props. There are no DTO endpoints, fetch hooks or client-side router to keep in sync with the server.',
  },
  {
    title: 'Interfaces your users will like',
    body: 'Write the UI in React, Vue or Svelte with their component libraries and tooling. Navigation stays fast, and server-side rendering makes the first load quick.',
  },
  {
    title: 'Easier to maintain',
    body: 'Routing, auth and validation stay in C#. Shared TypeScript types come from your .NET contracts, so a renamed property shows up as a compile error.',
  },
  {
    title: 'Faster with AI agents',
    body: 'One repo, one request flow and a small set of conventions. Agents can add a page end to end, and the docs ship an agent guide and llms.txt.',
  },
]

const steps = [
  {
    title: 'Return a page from C#',
    body: 'An endpoint, controller action or FastEndpoint calls Render with a component name and props.',
  },
  {
    title: 'Render it with your framework',
    body: 'Inertia resolves the component in your frontend and passes the props in. The first visit is plain HTML.',
  },
  {
    title: 'Navigate without full reloads',
    body: 'Links and forms make Inertia requests. The server answers with the next page and the client swaps it in.',
  },
]

const included = [
  ['Minimal APIs, MVC and FastEndpoints', '/docs/pages-and-responses'],
  ['Validation errors and Precognition', '/docs/validation-and-errors'],
  ['Deferred, merged, once and infinite-scroll props', '/docs/props'],
  ['Server-side rendering on Node or Bun', '/docs/server-side-rendering'],
  ['Vite dev server, manifests and prefetching', '/docs/vite-integration'],
  ['CSRF, flash data and encrypted history', '/docs/csrf-and-antiforgery'],
  ['Generated TypeScript types', '/docs/typescript'],
  ['Native AOT and test helpers', '/docs/native-aot'],
] as const

const code = {
  server: `app.MapGet("/", () => Render("Home", new InertiaProps
{
    ["greeting"] = "Hello from .NET",
}));`,
  client: `export default function Home({ greeting }: { greeting: string }) {
  return <h1>{greeting}</h1>
}`,
}

function CodePreview() {
  const [tab, setTab] = useState<keyof typeof code>('server')

  return (
    <div className="overflow-hidden rounded-lg border border-border bg-card">
      <div className="flex border-b border-border text-xs">
        {(['server', 'client'] as const).map((value) => (
          <button
            key={value}
            type="button"
            onClick={() => setTab(value)}
            className={cn(
              '-mb-px border-b-2 px-4 py-2.5 font-mono transition-colors',
              tab === value ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground hover:text-foreground',
            )}
          >
            {value === 'server' ? 'Program.cs' : 'Pages/Home.tsx'}
          </button>
        ))}
      </div>
      <pre className="min-h-[150px] overflow-x-auto p-5 font-mono text-[13px] leading-6">
        <code>{code[tab]}</code>
      </pre>
    </div>
  )
}

function Footer() {
  return (
    <footer className="border-t border-border">
      <div className="mx-auto flex max-w-[1100px] flex-col gap-4 px-6 py-8 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
        <p>Inertia.Net is MIT licensed. This site is built with it.</p>
        <ul className="flex flex-wrap gap-x-5 gap-y-2">
          <li><Link href="/docs/introduction" className="hover:text-foreground">Docs</Link></li>
          <li><a href="https://github.com/adzamkomladev/Inertia.Net" target="_blank" rel="noreferrer" className="hover:text-foreground">GitHub</a></li>
          <li><a href="https://www.nuget.org/packages/Inertia.Net" target="_blank" rel="noreferrer" className="hover:text-foreground">NuGet</a></li>
          <li><a href="https://github.com/adzamkomladev/Inertia.Net-Samples" target="_blank" rel="noreferrer" className="hover:text-foreground">Samples</a></li>
        </ul>
      </div>
    </footer>
  )
}

export default function Home() {
  return (
    <div className="min-h-screen">
      <Seo
        title="Inertia.Net — React, Vue and Svelte for ASP.NET Core"
        description="Inertia.Net lets .NET developers build React, Vue and Svelte frontends on ASP.NET Core without writing a separate API."
        type="website"
      />
      <SiteHeader />

      <main>
        <section className="border-b border-border">
          <div className="mx-auto grid max-w-[1100px] items-center gap-12 px-6 py-16 lg:grid-cols-[1.1fr_0.9fr] lg:py-24">
            <div>
              <p className="text-sm text-muted-foreground">Inertia.js v3 adapter for .NET 10</p>
              <h1 className="mt-4 max-w-xl text-balance text-4xl font-semibold tracking-tight sm:text-5xl">
                Modern frontends for ASP.NET Core apps
              </h1>
              <p className="mt-5 max-w-lg text-pretty text-base leading-7 text-muted-foreground">
                Keep your routes, auth and validation in C#. Build the UI in React, Vue or Svelte. Inertia.Net connects the two, so you skip the API layer and the client-side router.
              </p>
              <div className="mt-8 flex flex-wrap items-center gap-3">
                <Link
                  href="/docs/quick-start"
                  className="inline-flex h-10 items-center gap-2 rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
                >
                  Get started
                  <ArrowRight className="size-4" />
                </Link>
                <a
                  href="https://github.com/adzamkomladev/Inertia.Net"
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex h-10 items-center gap-2 rounded-md border border-border px-4 text-sm font-medium transition-colors hover:bg-accent"
                >
                  <GithubIcon className="size-4" />
                  GitHub
                </a>
              </div>
              <div className="mt-6">
                <InstallCommand command="dotnet add package Inertia.Net" />
              </div>
            </div>
            <CodePreview />
          </div>
        </section>

        <section className="border-b border-border">
          <div className="mx-auto max-w-[1100px] px-6 py-12">
            <p className="text-sm text-muted-foreground">Use the frontend you already know. The server code is the same for all three.</p>
            <ul className="mt-6 grid gap-3 sm:grid-cols-3">
              {frameworks.map(({ name, package: pkg, Logo }) => (
                <li key={name} className="flex items-center gap-4 rounded-lg border border-border p-4">
                  <Logo className="size-9 shrink-0" />
                  <div>
                    <p className="font-medium">{name}</p>
                    <p className="font-mono text-xs text-muted-foreground">{pkg}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section className="mx-auto max-w-[1100px] px-6 py-16 lg:py-20">
          <h2 className="max-w-xl text-2xl font-semibold tracking-tight sm:text-3xl">
            The JavaScript ecosystem, without the extra work
          </h2>
          <p className="mt-3 max-w-2xl leading-7 text-muted-foreground">
            A separate SPA usually means an API, a client router, auth on both sides and duplicated validation. Inertia.Net removes that layer so you can spend the time on the product.
          </p>
          <div className="mt-10 grid gap-x-10 gap-y-8 sm:grid-cols-2">
            {reasons.map(({ title, body }) => (
              <div key={title}>
                <h3 className="font-semibold">{title}</h3>
                <p className="mt-1.5 text-sm leading-6 text-muted-foreground">{body}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="border-y border-border bg-muted/40">
          <div className="mx-auto max-w-[1100px] px-6 py-16 lg:py-20">
            <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">How it works</h2>
            <ol className="mt-10 grid gap-8 sm:grid-cols-3">
              {steps.map(({ title, body }, index) => (
                <li key={title}>
                  <span className="font-mono text-sm text-muted-foreground">{index + 1}</span>
                  <h3 className="mt-2 font-semibold">{title}</h3>
                  <p className="mt-1.5 text-sm leading-6 text-muted-foreground">{body}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        <section className="mx-auto grid max-w-[1100px] gap-10 px-6 py-16 lg:grid-cols-[0.8fr_1.2fr] lg:py-20">
          <div>
            <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">Ready for production</h2>
            <p className="mt-3 leading-7 text-muted-foreground">
              The adapter covers the whole Inertia v3 protocol and the parts a real app needs around it.
            </p>
          </div>
          <ul className="grid gap-x-8 sm:grid-cols-2">
            {included.map(([label, href]) => (
              <li key={label} className="border-b border-border">
                <Link href={href} className="flex items-center justify-between gap-3 py-3 text-sm transition-colors hover:text-primary">
                  {label}
                  <ArrowRight className="size-3.5 shrink-0 text-muted-foreground" />
                </Link>
              </li>
            ))}
          </ul>
        </section>

        <section className="border-t border-border">
          <div className="mx-auto max-w-[1100px] px-6 py-16 lg:py-20">
            <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">Working with an AI agent?</h2>
            <p className="mt-3 max-w-2xl leading-7 text-muted-foreground">
              Give your coding agent the conventions it needs to add pages, props and forms correctly. The agent guide has a ready-made <code className="font-mono text-[0.9em]">AGENTS.md</code> section, and the whole documentation is available as plain text.
            </p>
            <div className="mt-6 flex flex-wrap gap-3 text-sm">
              <Link href="/docs/ai-agents" className="inline-flex h-10 items-center gap-2 rounded-md border border-border px-4 font-medium transition-colors hover:bg-accent">
                Read the agent guide
                <ArrowRight className="size-4" />
              </Link>
              <a href="/llms.txt" className="inline-flex h-10 items-center rounded-md px-3 font-mono text-muted-foreground transition-colors hover:text-foreground">
                /llms.txt
              </a>
              <a href="/llms-full.txt" className="inline-flex h-10 items-center rounded-md px-3 font-mono text-muted-foreground transition-colors hover:text-foreground">
                /llms-full.txt
              </a>
            </div>
          </div>
        </section>
      </main>

      <Footer />
    </div>
  )
}
