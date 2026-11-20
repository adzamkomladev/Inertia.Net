import { Check, ChevronLeft, ChevronRight, FileCode2, Globe, Hourglass, LoaderCircle, Lock, MousePointerClick, Pause, Play, Radio, RotateCw, type LucideIcon } from 'lucide-react'
import { useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode, type Ref } from 'react'
import dotnet from '../assets/logos/dotnet.svg'
import { cn } from '../lib/utils'
import { ReactLogo, SvelteLogo, VueLogo } from './icons/FrameworkLogos'

type Tone = 'req' | 'res' | 'live'
type PageKind = 'blank' | 'users' | 'user' | 'dash'
/** out: browser to server. in: server to browser. emerge: out of the server, then rests on the wire. hold: rests. deliver: from the wire into the browser. */
type Motion = 'out' | 'in' | 'emerge' | 'hold' | 'deliver'
type Card = { id: string; tone: Tone; label: string; meta: string; motion: Motion; lines: string[] }
type SpanKind = 'root' | 'code' | 'db' | 'render' | 'ssr' | 'live'
type Span = { name: string; kind: SpanKind; from: number; to: number; note?: string; slow?: boolean; at?: number }
type TraceKey = 'users' | 'user' | 'dash' | 'stats' | 'order'
type Step = {
  title: string
  /** Inline `code` is set in mono. */
  text: string
  ms?: number
  url: string
  page: PageKind
  /** The page is server-rendered HTML that has not hydrated yet. */
  ssr?: boolean
  /** fill: the values arrive with this step's card. */
  stats?: 'wait' | 'fill' | 'ready' | 'bumped'
  typing?: boolean
  /** A document request is in flight, so the browser shows its own spinner. */
  doc?: boolean
  /** Inertia's progress bar. */
  nav?: 'run' | 'done'
  cursor?: boolean
  hydrate?: boolean
  /** The live badge receives an event. */
  live?: boolean
  code: keyof typeof code
  hl?: [number, number]
  note?: { line: number; text: string }
  trace?: TraceKey
  spans?: number
  /** The request span has closed. */
  done?: boolean
  card?: Card
}
type Scene = { id: string; label: string; icon: LucideIcon; steps: Step[] }

const code = {
  users: [
    'app.MapGet("/users", (AppDb db) =>',
    '  Render("Users/Index", new InertiaProps',
    '  {',
    '    ["users"] = db.Users.ToList(),',
    '  }));',
    '',
    'app.MapGet("/users/{id}", (int id, AppDb db) =>',
    '  Render("Users/Show", new InertiaProps',
    '  {',
    '    ["user"] = db.Users.Find(id),',
    '  }));',
  ],
  dash: [
    'app.MapGet("/dashboard", (AppDb db) =>',
    '  Render("Dashboard", new InertiaProps',
    '  {',
    '    ["user"]  = db.CurrentUser(),',
    '    ["stats"] = Defer(ct => db.StatsAsync(ct))',
    '                  .Live(Orders.Placed),',
    '  }));',
    '',
    'app.MapPost("/orders", async (Order order,',
    '  AppDb db, IInertiaLive live) =>',
    '{',
    '  db.Orders.Add(order);',
    '  await db.SaveChangesAsync();',
    '  await live.PublishAsync(Orders.Placed);',
    '  return Back();',
    '});',
  ],
}

const traces: Record<TraceKey, { title: string; spans: Span[] }> = {
  users: {
    title: 'GET /users',
    spans: [
      { name: 'GET /users', kind: 'root', from: 0, to: 100 },
      { name: 'endpoint', kind: 'code', from: 2, to: 52 },
      { name: 'db.Users', kind: 'db', from: 8, to: 44 },
      { name: 'Render', kind: 'render', from: 52, to: 60 },
      { name: 'SSR · Bun', kind: 'ssr', from: 60, to: 97 },
    ],
  },
  user: {
    title: 'GET /users/2 · X-Inertia',
    spans: [
      { name: 'GET /users/2', kind: 'root', from: 0, to: 100 },
      { name: 'endpoint', kind: 'code', from: 3, to: 78 },
      { name: 'db.Users.Find', kind: 'db', from: 10, to: 66 },
      { name: 'Render → JSON', kind: 'render', from: 78, to: 96 },
      { name: 'SSR', kind: 'ssr', from: 0, to: 0, note: 'skipped' },
    ],
  },
  dash: {
    title: 'GET /dashboard',
    spans: [
      { name: 'GET /dashboard', kind: 'root', from: 0, to: 100 },
      { name: 'endpoint', kind: 'code', from: 2, to: 40 },
      { name: 'db.CurrentUser', kind: 'db', from: 7, to: 32 },
      { name: 'stats', kind: 'db', from: 40, to: 40, note: 'deferred' },
      { name: 'Render', kind: 'render', from: 40, to: 48 },
      { name: 'SSR · Bun', kind: 'ssr', from: 48, to: 96 },
    ],
  },
  stats: {
    title: 'GET /dashboard · only: stats',
    spans: [
      { name: 'GET /dashboard', kind: 'root', from: 0, to: 100, slow: true },
      { name: 'db.StatsAsync', kind: 'db', from: 4, to: 86, slow: true, at: 0.15 },
      { name: 'Render → JSON', kind: 'render', from: 86, to: 96, at: 4 },
    ],
  },
  order: {
    title: 'POST /orders · another visitor',
    spans: [
      { name: 'POST /orders', kind: 'root', from: 0, to: 100 },
      { name: 'endpoint', kind: 'code', from: 3, to: 95 },
      { name: 'db.SaveChanges', kind: 'db', from: 8, to: 58 },
      { name: 'live.Publish', kind: 'live', from: 62, to: 90 },
    ],
  },
}

const pageObject = ['{', '  "component": "Users/Index",', '  "props": { "users": […] },', '  "url": "/users",', '  "version": "a1f9"', '}']
const usersHtml = ['<div id="app">', '  <h1>Users</h1><ul>…</ul>', '</div>', '<script data-page="app">', '  {"component":"Users/Index",…}', '</script>']
const userJson = ['{', '  "component": "Users/Show",', '  "props": { "user": {…} },', '  "url": "/users/2",', '  "version": "a1f9"', '}']
const dashHtml = ['<div id="app">…</div>', '<script data-page="app">{', '  "component": "Dashboard",', '  "props": { "user": {…} },', '  "deferredProps": {', '    "default": ["stats"] }', '}</script>']
const statsJson = (orders: string) => ['{', '  "component": "Dashboard",', '  "props": {', `    "stats": { "orders": ${orders}, … }`, '  }', '}']
const partial = ['GET /dashboard', 'X-Inertia: true', 'X-Inertia-Partial-Data: stats']

const scenes: Scene[] = [
  {
    id: 'first',
    label: 'First visit',
    icon: Globe,
    steps: [
      { title: 'The browser asks for /users', text: 'A normal navigation. Nothing has loaded yet, so the page is blank.', url: '/users', page: 'blank', typing: true, doc: true, code: 'users', card: { id: 'f1', tone: 'req', label: 'Request', meta: 'document', motion: 'out', lines: ['GET /users', 'Accept: text/html'] } },
      { title: 'Your endpoint runs', text: 'An ordinary Minimal API, controller or FastEndpoint loads the users and calls `Render("Users/Index", props)`.', url: '/users', page: 'blank', doc: true, code: 'users', hl: [0, 4], trace: 'users', spans: 3 },
      { title: 'Inertia.Net builds the page object', text: 'The adapter wraps the result as one page object: the component name, its props, the URL and the asset version.', url: '/users', page: 'blank', doc: true, code: 'users', hl: [1, 4], note: { line: 4, text: 'page object' }, trace: 'users', spans: 4, card: { id: 'f3', tone: 'res', label: 'Page object', meta: 'Inertia.Net', motion: 'emerge', lines: pageObject } },
      { title: 'The SSR server renders it', text: 'On a first visit the page object goes to Bun or Node, which renders your real React, Vue or Svelte component to HTML.', url: '/users', page: 'blank', doc: true, code: 'users', trace: 'users', spans: 5, card: { id: 'f3', tone: 'res', label: 'HTML', meta: 'SSR · Bun', motion: 'hold', lines: usersHtml } },
      { title: 'HTML comes back with the data embedded', text: 'The browser paints complete markup at once. The page object rides along in a `<script data-page>` element.', url: '/users', page: 'users', ssr: true, doc: true, code: 'users', trace: 'users', spans: 5, done: true, card: { id: 'f3', tone: 'res', label: 'Response', meta: '200 · text/html', motion: 'deliver', lines: usersHtml } },
      { title: 'The Inertia client takes over', text: 'It boots from the embedded page object and hydrates the component. Links and forms now go through Inertia, with no second fetch.', url: '/users', page: 'users', hydrate: true, code: 'users', trace: 'users', spans: 5, done: true },
    ],
  },
  {
    id: 'nav',
    label: 'Click a link',
    icon: MousePointerClick,
    steps: [
      { title: 'A link is clicked', text: 'The Inertia client intercepts the click on Grace Hopper, so the browser does not load a new document.', url: '/users', page: 'users', cursor: true, code: 'users' },
      { title: 'It sends a request with X-Inertia', text: 'The `X-Inertia` header tells the server the caller is the Inertia client, which wants JSON rather than a whole document.', url: '/users', page: 'users', nav: 'run', code: 'users', card: { id: 'n2', tone: 'req', label: 'Request', meta: 'XHR', motion: 'out', lines: ['GET /users/2', 'X-Inertia: true', 'X-Inertia-Version: a1f9'] } },
      { title: 'The same endpoint code runs', text: 'Routing and data access work exactly as on a first visit. The adapter sees the header and skips the HTML shell and the SSR server.', url: '/users', page: 'users', nav: 'run', code: 'users', hl: [6, 10], note: { line: 10, text: 'JSON, no SSR' }, trace: 'user', spans: 5 },
      { title: 'Only JSON goes back', text: 'The response is the page object and nothing else: a few hundred bytes instead of a whole HTML page.', url: '/users', page: 'users', nav: 'done', code: 'users', trace: 'user', spans: 5, done: true, card: { id: 'n4', tone: 'res', label: 'Response', meta: '200 · JSON', motion: 'in', lines: userJson } },
      { title: 'The client swaps the component', text: 'It renders `Users/Show` with the new props and pushes /users/2 onto the history. The page never reloaded.', url: '/users/2', page: 'user', code: 'users', trace: 'user', spans: 5, done: true },
    ],
  },
  {
    id: 'defer',
    label: 'Deferred props',
    icon: Hourglass,
    steps: [
      { title: 'A dashboard with a slow query', text: 'The visitor opens /dashboard. One prop, the stats, takes a while to compute.', url: '/dashboard', page: 'blank', typing: true, doc: true, code: 'dash', card: { id: 'd1', tone: 'req', label: 'Request', meta: 'document', motion: 'out', lines: ['GET /dashboard', 'Accept: text/html'] } },
      { title: 'The slow prop is left out', text: '`Defer(...)` props are not computed for the first response. The adapter lists them under `deferredProps` and answers straight away.', url: '/dashboard', page: 'blank', doc: true, code: 'dash', hl: [0, 6], note: { line: 6, text: 'stats deferred' }, trace: 'dash', spans: 6 },
      { title: 'The page shows up at once', text: 'The server-rendered dashboard arrives with placeholders where the stats will go.', url: '/dashboard', page: 'dash', ssr: true, stats: 'wait', doc: true, code: 'dash', trace: 'dash', spans: 6, done: true, card: { id: 'd3', tone: 'res', label: 'Response', meta: '200 · text/html', motion: 'in', lines: dashHtml } },
      { title: 'The client asks for the rest', text: 'As soon as the page mounts it sends a partial reload for just the deferred props. The page stays interactive meanwhile.', url: '/dashboard', page: 'dash', stats: 'wait', code: 'dash', card: { id: 'd4', tone: 'req', label: 'Partial reload', meta: 'XHR', motion: 'out', lines: partial } },
      { title: 'Only the slow query runs', text: 'The adapter runs the stats loader. Nothing else on the page is recomputed.', ms: 7000, url: '/dashboard', page: 'dash', stats: 'wait', code: 'dash', hl: [4, 4], note: { line: 6, text: 'only stats' }, trace: 'stats', spans: 3 },
      { title: 'The placeholders fill in', text: 'The stats arrive as JSON and the component re-renders with real numbers.', url: '/dashboard', page: 'dash', stats: 'fill', code: 'dash', trace: 'stats', spans: 3, done: true, card: { id: 'd6', tone: 'res', label: 'Response', meta: '200 · JSON', motion: 'in', lines: statsJson('1284') } },
    ],
  },
  {
    id: 'live',
    label: 'Live props',
    icon: Radio,
    steps: [
      { title: 'Something changes on the server', text: 'Another customer places an order. The handler saves it and publishes `OrderPlaced` through `IInertiaLive`. Nobody is polling.', url: '/dashboard', page: 'dash', stats: 'ready', code: 'dash', hl: [11, 13], note: { line: 13, text: 'publish' }, trace: 'order', spans: 4 },
      { title: 'The event reaches the browser', text: 'The SignalR transport delivers it to every open page with a prop that listens to `OrderPlaced`.', url: '/dashboard', page: 'dash', stats: 'ready', live: true, code: 'dash', trace: 'order', spans: 4, done: true, card: { id: 'l2', tone: 'live', label: 'Live event', meta: 'SignalR', motion: 'in', lines: ['OrderPlaced', 'channel: orders', 'listeners: stats'] } },
      { title: 'The client reloads just that prop', text: 'It sends a partial reload for the stats, the one prop listening to the event.', url: '/dashboard', page: 'dash', stats: 'ready', code: 'dash', card: { id: 'l3', tone: 'req', label: 'Partial reload', meta: 'XHR', motion: 'out', lines: partial } },
      { title: 'Fresh data comes back', text: 'The adapter runs only the stats loader and answers with that prop alone.', url: '/dashboard', page: 'dash', stats: 'ready', code: 'dash', hl: [4, 5], note: { line: 6, text: 'only stats' }, trace: 'stats', spans: 3, done: true, card: { id: 'l4', tone: 'res', label: 'Response', meta: '200 · JSON', motion: 'in', lines: statsJson('1285') } },
      { title: 'The numbers update in place', text: 'The component re-renders with the new stats. No component subscribed to anything, and nothing was polled.', url: '/dashboard', page: 'dash', stats: 'bumped', code: 'dash' },
    ],
  },
]

const frameworks = [
  { name: 'React', ext: 'tsx', Logo: ReactLogo },
  { name: 'Vue', ext: 'vue', Logo: VueLogo },
  { name: 'Svelte', ext: 'svelte', Logo: SvelteLogo },
]

const people = [
  { name: 'Ada Lovelace', email: 'ada@acme.dev', role: 'Admin', tone: 'primary' },
  { name: 'Grace Hopper', email: 'grace@acme.dev', role: 'Editor', tone: 'info' },
  { name: 'Alan Turing', email: 'alan@acme.dev', role: 'Viewer', tone: 'success' },
  { name: 'Katherine Johnson', email: 'katherine@acme.dev', role: 'Editor', tone: 'warning' },
  { name: 'Margaret Hamilton', email: 'margaret@acme.dev', role: 'Viewer', tone: 'danger' },
]

const kpis = [
  { label: 'Revenue', value: '$48,210', next: '$48,339' },
  { label: 'Orders', value: '1,284', next: '1,285', bump: true },
  { label: 'Customers', value: '312', next: '312' },
]
const week = [0.42, 0.55, 0.38, 0.7, 0.62, 0.84, 0.58, 0.74, 0.5, 0.66, 0.9, 0.62]

/** Seconds from the start of a step until its card disappears into the far window. */
const arrival: Partial<Record<Motion, number>> = { out: 3.3, in: 3.3, deliver: 1.8 }
const component: Record<PageKind, string> = { blank: '', users: 'Users/Index', user: 'Users/Show', dash: 'Dashboard' }

const vars = (o: Record<string, string | number | undefined>) => o as CSSProperties
const reduced = () => typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches

const TOKEN = /("(?:[^"\\]|\\.)*")(\s*:)?|\b(new|async|await|return|int)\b|([A-Za-z_]\w*)(?=\()|\b([A-Z]\w*)\b|(<\/?[a-z]+|\/?>)/g

/** A tiny highlighter for the C#, JSON and HTML on screen, using the same palette as the docs code blocks. */
function Tokens({ text }: { text: string }) {
  const out: ReactNode[] = []
  let last = 0
  for (const m of text.matchAll(TOKEN)) {
    if (m.index > last) out.push(text.slice(last, m.index))
    if (m[1]) out.push(<span key={m.index} className={m[2] ? 'ia-t-key' : 'ia-t-str'}>{m[1]}</span>, m[2] ?? '')
    else out.push(<span key={m.index} className={m[3] ? 'ia-t-kw' : m[4] ? 'ia-t-fn' : m[5] ? 'ia-t-type' : 'ia-t-tag'}>{m[0]}</span>)
    last = m.index + m[0].length
  }
  out.push(text.slice(last))
  return <>{out}</>
}

/** Caption text with `inline code`. */
function rich(text: string) {
  return text.split('`').map((part, i) => (i % 2 ? <code key={i} className="rounded bg-muted px-1 py-px font-mono text-[0.86em] text-foreground">{part}</code> : part))
}

/** Shows `before` until the incoming card lands, then `after`. Without a landing it just shows `after`. */
function Swap({ landing, before, after }: { landing: boolean; before: ReactNode; after: ReactNode }) {
  if (!landing) return <>{after}</>
  return (
    <span className="ia-stack">
      <span className="ia-until-land">{before}</span>
      <span className="ia-from-land">{after}</span>
    </span>
  )
}

function Avatar({ name, tone, big, ref }: { name: string; tone: string; big?: boolean; ref?: Ref<HTMLSpanElement> }) {
  return (
    <span
      ref={ref}
      className={cn('grid shrink-0 place-items-center rounded-full font-semibold', big ? 'size-11 text-[13px]' : 'size-7 text-[10px]')}
      style={{ background: `color-mix(in oklab, var(--${tone}) 20%, var(--card))`, color: `var(--${tone})`, transformOrigin: '0 0' }}
    >
      {name.split(' ').map((w) => w[0]).join('')}
    </span>
  )
}

/** Digits roll like an odometer: up from zero when they first appear, then one notch at a time. */
function Odometer({ value }: { value: string }) {
  let k = 0
  return (
    <span className="ia-odo" aria-label={value}>
      {[...value].map((ch, i) =>
        /\d/.test(ch) ? (
          <span key={i} className="ia-digit" aria-hidden="true">
            <span style={vars({ '--d': ch, '--k': k++ })}>
              {'0123456789'.split('').map((d) => (
                <span key={d}>{d}</span>
              ))}
            </span>
          </span>
        ) : (
          <span key={i} aria-hidden="true">
            {ch}
          </span>
        ),
      )}
    </span>
  )
}

/** Remembers how the page first arrived, so hydrating later does not replay the entrance. */
function AppFrame({ paint, children }: { paint: boolean; children: ReactNode }) {
  const [painted] = useState(paint)
  return (
    <div className="ia-page" data-paint={painted ? '' : undefined}>
      {children}
    </div>
  )
}

function Browser({ step, runKey, toBrowser, fw, onFw, navKey }: { step: Step; runKey: string; toBrowser: boolean; fw: number; onFw: () => void; navKey: string }) {
  const vp = useRef<HTMLDivElement>(null)
  const graceRow = useRef<HTMLLIElement>(null)
  const graceAvatar = useRef<HTMLSpanElement>(null)
  const bigAvatar = useRef<HTMLSpanElement>(null)
  const from = useRef<{ x: number; y: number; w: number } | null>(null)
  const prevPage = useRef(step.page)
  const [aim, setAim] = useState<{ x: number; y: number } | null>(null)

  // Shared-element morph: Grace's avatar flies from her row into the profile header.
  useLayoutEffect(() => {
    const box = vp.current?.getBoundingClientRect()
    if (!box) return
    if (step.page === 'users' && graceAvatar.current) {
      const r = graceAvatar.current.getBoundingClientRect()
      from.current = { x: r.left - box.left, y: r.top - box.top, w: r.width }
    }
    if (prevPage.current === 'users' && step.page === 'user' && bigAvatar.current && from.current && !reduced()) {
      const r = bigAvatar.current.getBoundingClientRect()
      const f = from.current
      bigAvatar.current.animate(
        [{ transform: `translate(${f.x - (r.left - box.left)}px, ${f.y - (r.top - box.top)}px) scale(${f.w / r.width})` }, { transform: 'none' }],
        { duration: 950, easing: 'cubic-bezier(0.25, 1, 0.5, 1)' },
      )
    }
    prevPage.current = step.page
  })

  useLayoutEffect(() => {
    const box = vp.current?.getBoundingClientRect()
    const row = graceRow.current?.getBoundingClientRect()
    setAim(step.cursor && box && row ? { x: row.left - box.left + row.width * 0.38, y: row.top - box.top + row.height * 0.55 } : null)
  }, [step.cursor, runKey])

  const hydrated = step.page !== 'blank' && !step.ssr
  const F = frameworks[fw]
  const paint = toBrowser && step.ssr
  const file = `Pages/${component[step.page]}.${F.ext}`

  const waiting = (
    <span className="inline-flex items-center gap-1.5">
      <LoaderCircle className="ia-spin size-3" />
      Waiting for {step.url}
    </span>
  )
  const ssrStatus = (
    <span className="inline-flex items-center gap-1.5">
      <FileCode2 className="size-3" />
      Server-rendered HTML
      <span className="hidden text-muted-foreground/70 sm:inline">· not interactive yet</span>
    </span>
  )

  return (
    <div className="ia-win">
      <div className="ia-chrome" aria-hidden="true">
        <span className="ia-dots">
          <i />
          <i />
          <i />
        </span>
        <span className="flex text-muted-foreground">
          <ChevronLeft className={cn('size-3.5 transition-colors duration-500', step.page === 'user' ? 'text-foreground' : 'opacity-40')} />
          <ChevronRight className="size-3.5 opacity-40" />
        </span>
        <span className="ia-url">
          <Lock className="size-2.5 shrink-0 text-muted-foreground" />
          <span className="min-w-0 truncate">
            <span className="text-muted-foreground">acme.app</span>
            {step.typing ? (
              <span key={runKey} className="ia-type" style={vars({ '--n': step.url.length })}>
                {step.url}
              </span>
            ) : (
              <span key={step.url} className={cn(step.page === 'user' && 'ia-path-flash')}>
                {step.url}
              </span>
            )}
          </span>
          <span className="ml-auto grid shrink-0 text-muted-foreground">
            {step.doc ? (
              <Swap landing={toBrowser} before={<LoaderCircle className="ia-spin size-3" />} after={<RotateCw className="size-3" />} />
            ) : (
              <RotateCw className="size-3" />
            )}
          </span>
        </span>
      </div>

      <div ref={vp} className="ia-viewport" data-hydrated={hydrated ? '' : undefined} aria-hidden="true">
        {step.nav && <span key={navKey} className="ia-prog" data-state={step.nav} />}
        {step.page !== 'blank' && (
          <AppFrame key={step.page === 'dash' ? 'dash' : 'app'} paint={!!paint}>
            <div className="flex h-10 shrink-0 items-center gap-2 border-b border-border px-4">
              <span className="grid size-5 place-items-center rounded-md bg-primary text-[10px] font-bold text-primary-foreground">a</span>
              <span className="text-[12px] font-semibold">Acme</span>
              <span className="relative ml-3 flex text-[11px] text-muted-foreground">
                <span className={cn('w-[68px] text-center transition-colors', step.page !== 'dash' && 'text-foreground')}>Users</span>
                <span className={cn('w-[68px] text-center transition-colors', step.page === 'dash' && 'text-foreground')}>Dashboard</span>
                <span className="ia-tabline" style={vars({ '--x': step.page === 'dash' ? 1 : 0 })} />
              </span>
            </div>
            {step.page === 'users' && (
              <div key="users" className="ia-sub px-4 pt-3">
                <div className="ia-in flex items-baseline justify-between">
                  <span className="text-[14px] font-semibold tracking-tight">Users</span>
                  <span className="font-mono text-[10px] text-muted-foreground">1–5 of 25</span>
                </div>
                <ul className="mt-2">
                  {people.map((p, i) => (
                    <li key={p.name} ref={i === 1 ? graceRow : undefined} className={cn('ia-row ia-in', i === 1 && step.cursor && 'ia-target')} style={vars({ '--i': i + 1 })}>
                      <Avatar name={p.name} tone={p.tone} ref={i === 1 ? graceAvatar : undefined} />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-[12px] font-medium leading-4">{p.name}</span>
                        <span className="block truncate text-[10.5px] leading-4 text-muted-foreground">{p.email}</span>
                      </span>
                      <span className="rounded-full border border-border px-1.5 text-[9.5px] leading-4 text-muted-foreground">{p.role}</span>
                      <ChevronRight className="ia-chev size-3.5 text-muted-foreground" style={vars({ '--i': i })} />
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {step.page === 'user' && (
              <div key="user" className="ia-sub px-4 pt-3">
                <span className="ia-in inline-flex items-center gap-0.5 text-[10.5px] text-muted-foreground">
                  <ChevronLeft className="size-3" />
                  Users
                </span>
                <div className="mt-2.5 flex items-center gap-3">
                  <Avatar name={people[1].name} tone={people[1].tone} big ref={bigAvatar} />
                  <div className="ia-in" style={vars({ '--i': 1 })}>
                    <p className="text-[15px] font-semibold tracking-tight">Grace Hopper</p>
                    <p className="text-[10.5px] text-muted-foreground">grace@acme.dev · Editor</p>
                  </div>
                </div>
                <div className="mt-4 grid grid-cols-3 gap-2">
                  {[
                    ['Orders', '38'],
                    ['Member since', '2021'],
                    ['Team', 'Core'],
                  ].map(([k, v], i) => (
                    <div key={k} className="ia-in rounded-lg border border-border p-2" style={vars({ '--i': i + 2 })}>
                      <p className="truncate text-[9.5px] text-muted-foreground">{k}</p>
                      <p className="mt-0.5 text-[14px] font-semibold tabular-nums">{v}</p>
                    </div>
                  ))}
                </div>
                <p className="ia-in mt-4 text-[10px] font-medium text-muted-foreground" style={vars({ '--i': 5 })}>
                  Recent activity
                </p>
                {['Placed order #1039', 'Updated billing address'].map((t, i) => (
                  <p key={t} className="ia-in mt-1.5 flex items-center gap-2 text-[11px]" style={vars({ '--i': 6 + i })}>
                    <span className="size-1.5 rounded-full bg-primary/60" />
                    {t}
                    <span className="ml-auto font-mono text-[9.5px] text-muted-foreground">{i ? '3d' : '2h'}</span>
                  </p>
                ))}
              </div>
            )}
            {step.page === 'dash' && (
              <div className="ia-sub px-4 pt-3">
                <div className="ia-in flex items-center justify-between">
                  <div>
                    <p className="text-[10px] text-muted-foreground">Good morning, Ada</p>
                    <p className="text-[14px] font-semibold tracking-tight">Dashboard</p>
                  </div>
                  {hydrated && (
                    <span key={step.live ? runKey : 'live'} className="ia-live" data-ping={step.live ? '' : undefined}>
                      <span className="ia-live-dot" />
                      Live
                      {step.live && <span className="ia-live-evt">· OrderPlaced</span>}
                    </span>
                  )}
                </div>
                <div className="mt-3 grid grid-cols-3 gap-2">
                  {kpis.map((k, i) => {
                    const bump = step.stats === 'bumped' && k.bump
                    return (
                      <div key={k.label} className={cn('ia-in relative rounded-lg border border-border p-2', bump && 'ia-bump')} style={vars({ '--i': i + 1 })}>
                        <p className="truncate text-[9.5px] text-muted-foreground">{k.label}</p>
                        <div className="mt-1 flex h-[1.25em] items-center text-[14px] font-semibold tabular-nums">
                          {step.stats === 'wait' ? (
                            <span className="ia-skel h-3 w-4/5 rounded" />
                          ) : (
                            <Swap landing={step.stats === 'fill'} before={<span className="ia-skel block h-3 w-12 rounded" />} after={<Odometer value={step.stats === 'bumped' ? k.next : k.value} />} />
                          )}
                        </div>
                        {bump && <span className="ia-plus">+1</span>}
                      </div>
                    )
                  })}
                </div>
                <div className="ia-in mt-3 rounded-lg border border-border p-2.5" style={vars({ '--i': 4 })}>
                  <p className="text-[9.5px] text-muted-foreground">Orders this week</p>
                  <div className="mt-2 flex h-14 items-end gap-1">
                    {week.map((h, i) => {
                      const hot = step.stats === 'bumped' && i === week.length - 1
                      return <span key={i} className={cn('ia-bar', step.stats === 'wait' && 'ia-skel', hot && 'ia-bar-hot')} style={vars({ '--h': step.stats === 'wait' ? 0.22 : hot ? 1 : h, '--i': i })} />
                    })}
                  </div>
                </div>
              </div>
            )}
          </AppFrame>
        )}
        {step.hydrate && <span key={runKey} className="ia-sweep" />}
        {aim && (
          <>
            <span key={`${runKey}c`} className="ia-click" style={vars({ left: `${aim.x}px`, top: `${aim.y}px` })} />
            <svg key={runKey} className="ia-cursor" style={vars({ left: `${aim.x}px`, top: `${aim.y}px` })} viewBox="0 0 16 22" width="16" height="22">
              <path d="M1 1 1 17.5 5.2 13.6 8.1 20.2 10.9 19 8.1 12.5 13.8 12.3Z" fill="#fff" stroke="#111" strokeWidth="1.3" strokeLinejoin="round" />
            </svg>
          </>
        )}
      </div>

      <div className="ia-status" aria-live="off">
        {step.page === 'blank' ? (
          step.doc && <span aria-hidden="true">{waiting}</span>
        ) : step.ssr ? (
          <span aria-hidden="true">
            <Swap landing={toBrowser} before={waiting} after={ssrStatus} />
          </span>
        ) : (
          <>
            <button
              type="button"
              onClick={onFw}
              title="Switch the frontend. The server code stays the same."
              aria-label={`Frontend: ${F.name}. Switch framework`}
              className="ia-fw"
            >
              <F.Logo key={fw} className="ia-pop size-3.5" />
              {F.name}
            </button>
            <span className="min-w-0 truncate font-mono text-[10px]" aria-hidden="true">{file}</span>
            <span className="ml-auto hidden shrink-0 items-center gap-1.5 sm:inline-flex" aria-hidden="true">
              <span className="size-1.5 rounded-full bg-success" />
              interactive
            </span>
          </>
        )}
      </div>
    </div>
  )
}

function TraceRow({ s, i, before, done, maxTo }: { s: Span; i: number; before: number; done: boolean; maxTo: number }) {
  const root = s.kind === 'root'
  const style = vars({ '--from': s.from, '--to': s.to, '--w': done ? 1 : Math.min(1, (maxTo + 3) / 100), '--delay': `${s.at ?? Math.max(0, i - before) * 0.4}s` })
  return (
    <div className="ia-span">
      <span className={cn('truncate', root ? 'font-semibold text-foreground' : 'text-muted-foreground', s.note === 'skipped' && 'line-through opacity-60')} style={{ paddingLeft: root ? 0 : 8 }}>
        {s.name}
      </span>
      <span className="ia-track">
        {s.note ? (
          <span className="ia-span-note" style={style}>
            {s.note}
          </span>
        ) : (
          <span className={cn('ia-bar-span', `ia-k-${s.kind}`, root && 'ia-span-root')} data-open={root && !done ? '' : undefined} data-slow={s.slow ? '' : undefined} style={style} />
        )}
      </span>
    </div>
  )
}

function Server({ step, prev, toServer, runKey, fwTick }: { step: Step; prev?: Step; toServer: boolean; runKey: string; fwTick: number }) {
  const lines = code[step.code]
  const hl = step.hl
  const trace = step.trace ? traces[step.trace] : null
  const shown = trace ? trace.spans.slice(0, step.spans ?? trace.spans.length) : []
  const before = prev?.trace && prev.trace === step.trace ? (prev.spans ?? 0) : 0
  const maxTo = Math.max(0, ...shown.filter((s) => s.kind !== 'root').map((s) => s.to))
  const note = step.note

  return (
    <div className="ia-win" aria-hidden="true">
      <div className="ia-chrome">
        <span className="grid size-5 shrink-0 place-items-center rounded-[5px] border border-border bg-white">
          <img src={dotnet} alt="" className="size-4" />
        </span>
        <span className="text-[12px] font-semibold">ASP.NET Core</span>
        <span className="rounded-md border border-border bg-card px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">Program.cs</span>
        <span className="ia-glue ml-auto">Inertia.Net</span>
      </div>
      <div key={step.code} className="ia-code" data-hl={hl ? '' : undefined}>
        <span className="ia-hl" style={vars({ '--a': hl?.[0] ?? 0, '--n': hl ? hl[1] - hl[0] + 1 : 1, opacity: hl ? 1 : 0 })} />
        {lines.map((l, i) => (
          <div key={i} className="ia-line" data-on={hl && i >= hl[0] && i <= hl[1] ? '' : undefined}>
            <span className="ia-gutter">{i + 1}</span>
            <span>
              <Tokens text={l} />
            </span>
          </div>
        ))}
        {note && (
          <span key={runKey} className="ia-note" style={vars({ '--a': note.line, '--len': lines[note.line].length, '--nw': `${note.text.length * 5.6 + 18}px` })}>
            <span>{note.text}</span>
          </span>
        )}
      </div>
      {fwTick > 0 && (
        <span key={fwTick} className="ia-toast">
          <Check className="size-3" />
          No server changes needed
        </span>
      )}
      <div className="ia-trace">
        <div className="flex items-center gap-2 text-[10px]">
          <span className="font-semibold uppercase tracking-[0.12em] text-muted-foreground">Trace</span>
          <span key={step.trace ?? 'idle'} className="ia-fade min-w-0 truncate font-mono text-foreground">
            {trace?.title ?? ''}
          </span>
          {trace && !step.done && <span className="ia-rec ml-auto" />}
        </div>
        <div className="mt-2">
          {trace ? (
            shown.map((s, i) => <TraceRow key={`${step.trace}-${i}`} s={s} i={i} before={before} done={!!step.done} maxTo={maxTo} />)
          ) : (
            <p key={runKey} className="ia-fade flex items-center gap-2 pt-1 text-[11px] text-muted-foreground">
              <span className="ia-idle" />
              {toServer ? 'Request incoming…' : 'Listening on :5000'}
            </p>
          )}
        </div>
      </div>
    </div>
  )
}

function Wire({ card, land, runKey }: { card?: Card; land: number; runKey: string }) {
  const out = card?.motion === 'out'
  const arriving = card && arrival[card.motion] !== undefined
  return (
    <div
      className="ia-wire"
      data-tone={card?.tone}
      data-busy={card && card.motion !== 'hold' ? '' : undefined}
      data-rest={card?.motion === 'emerge' || card?.motion === 'hold' ? '' : undefined}
      style={vars({ '--land': `${land}s` })}
      aria-hidden="true"
    >
      <span key={`${runKey}l`} className="ia-lane" />
      <span key={`${runKey}b`} className="ia-port ia-port-b" data-flash={out ? 'send' : arriving ? 'recv' : undefined} />
      <span key={`${runKey}s`} className="ia-port ia-port-s" data-flash={out ? 'recv' : card?.motion === 'in' || card?.motion === 'emerge' ? 'send' : undefined} />
      <span key={`${runKey}t`} className="ia-wire-label">
        Inertia protocol
      </span>
      <span key={`${runKey}u`} className="ia-wire-sub">
        X-Inertia · page object
      </span>
      {card && (
        <div className="ia-card-slot">
          <div key={card.id} className="ia-card" data-tone={card.tone} data-motion={card.motion}>
            <div className="ia-card-head">
              <span className="ia-card-dot" />
              {card.label}
              <span className="ia-card-meta">{card.meta}</span>
            </div>
            <div key={card.lines.join()} className="ia-card-body">
              {card.lines.map((l, i) => (
                <div key={i}>
                  <Tokens text={l} />
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/** The landing-page walkthrough: a real-looking browser and ASP.NET Core app trade the actual Inertia messages, one step at a time. */
export function ArchitectureDiagram() {
  const uid = useId().replace(/[^\w-]/g, '')
  const root = useRef<HTMLDivElement>(null)
  const tabs = useRef<HTMLDivElement>(null)
  const fill = useRef<HTMLElement>(null)
  const elapsed = useRef(0)
  const [scene, setScene] = useState(0)
  const [stepIndex, setStep] = useState(0)
  const [playing, setPlaying] = useState(true)
  const [inView, setInView] = useState(false)
  const [armed, setArmed] = useState(false)
  const [seen, setSeen] = useState(false)
  const [fw, setFw] = useState(0)
  const [fwTick, setFwTick] = useState(0)
  const [pill, setPill] = useState<{ x: number; w: number } | null>(null)

  const current = scenes[scene]
  const step = current.steps[stepIndex]
  const prev = current.steps[stepIndex - 1]
  const last = stepIndex === current.steps.length - 1
  const dur = (step.ms ?? 5800) + (last ? 2000 : 0)
  const runKey = `${scene}-${stepIndex}`
  const card = step.card
  const land = card ? (arrival[card.motion] ?? 0) : 0
  const toBrowser = card?.motion === 'in' || card?.motion === 'deliver'
  const toServer = card?.motion === 'out'
  let navStart = stepIndex
  while (navStart > 0 && current.steps[navStart - 1].nav === 'run') navStart--

  const go = (s: number, i: number) => {
    elapsed.current = 0
    fill.current?.style.setProperty('--p', '0')
    setScene(s)
    setStep(i)
  }
  const next = () => (last ? go((scene + 1) % scenes.length, 0) : go(scene, stepIndex + 1))
  const switchFramework = () => {
    setFw((fw + 1) % frameworks.length)
    setFwTick(fwTick + 1)
  }
  const back = () => (stepIndex > 0 ? go(scene, stepIndex - 1) : go((scene + scenes.length - 1) % scenes.length, scenes[(scene + scenes.length - 1) % scenes.length].steps.length - 1))

  useEffect(() => {
    if (reduced()) setPlaying(false)
    setArmed(true)
    const el = root.current
    if (!el) return
    const io = new IntersectionObserver(
      ([e]) => {
        setInView(e.isIntersecting)
        if (e.isIntersecting) setSeen(true)
      },
      { threshold: 0.25 },
    )
    io.observe(el)
    return () => io.disconnect()
  }, [])

  // One clock drives the step timer and the progress fill, so pausing freezes both exactly where they are.
  useEffect(() => {
    if (!playing || !inView) return
    let raf = 0
    let t = performance.now()
    const tick = (now: number) => {
      elapsed.current += now - t
      t = now
      fill.current?.style.setProperty('--p', String(Math.min(1, elapsed.current / dur)))
      if (elapsed.current >= dur) next()
      else raf = requestAnimationFrame(tick)
    }
    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  })

  // The active-tab pill glides between tabs.
  useLayoutEffect(() => {
    const list = tabs.current
    if (!list) return
    const tab = () => list.querySelectorAll<HTMLElement>('[role=tab]')[scene]
    const measure = () => {
      const b = tab()
      if (b) setPill({ x: b.offsetLeft, w: b.offsetWidth })
    }
    measure()
    const b = tab()
    if (b && list.scrollWidth > list.clientWidth) list.scrollTo({ left: b.offsetLeft - 8, behavior: 'smooth' })
    const ro = new ResizeObserver(measure)
    ro.observe(list)
    return () => ro.disconnect()
  }, [scene])

  return (
    <div ref={root} className="ia-root overflow-hidden rounded-2xl border border-border bg-card" data-armed={armed ? '' : undefined} data-in={seen ? '' : undefined}>
      <div className="flex items-center gap-2 border-b border-border p-2">
        <div
          ref={tabs}
          role="tablist"
          aria-label="Scenarios"
          className="no-scrollbar relative flex min-w-0 flex-1 gap-1 overflow-x-auto"
          onKeyDown={(e) => {
            const n = e.key === 'ArrowRight' ? scene + 1 : e.key === 'ArrowLeft' ? scene - 1 : e.key === 'Home' ? 0 : e.key === 'End' ? scenes.length - 1 : null
            if (n === null) return
            e.preventDefault()
            const s = (n + scenes.length) % scenes.length
            go(s, 0)
            document.getElementById(`${uid}-tab-${s}`)?.focus()
          }}
        >
          {pill && <span className="ia-tab-pill" style={vars({ translate: `${pill.x}px 0`, width: `${pill.w}px` })} />}
          {scenes.map((s, i) => (
            <button
              key={s.id}
              id={`${uid}-tab-${i}`}
              type="button"
              role="tab"
              aria-selected={i === scene}
              aria-controls={`${uid}-panel`}
              tabIndex={i === scene ? 0 : -1}
              onClick={() => go(i, 0)}
              className={cn(
                'relative z-[1] inline-flex h-9 shrink-0 items-center gap-2 rounded-full px-3.5 text-sm font-medium whitespace-nowrap transition-colors duration-300',
                i === scene ? (pill ? 'text-primary-foreground' : 'bg-primary text-primary-foreground') : 'text-muted-foreground hover:text-foreground',
              )}
            >
              <s.icon className="size-3.5" />
              {s.label}
            </button>
          ))}
        </div>
      </div>

      <div id={`${uid}-panel`} role="tabpanel" aria-labelledby={`${uid}-tab-${scene}`} className="ia-stage">
        <p className="sr-only">{`${current.label}, step ${stepIndex + 1} of ${current.steps.length}: ${step.title}. ${step.text.replaceAll('`', '')}`}</p>
        <div className="ia-cell ia-cell-b" style={vars({ '--land': toBrowser ? `${land}s` : '0s' })}>
          <Browser step={step} runKey={runKey} toBrowser={toBrowser} fw={fw} onFw={switchFramework} navKey={`${scene}-${navStart}`} />
          {toBrowser && <span key={runKey} className="ia-ring" data-tone={card?.tone} />}
        </div>
        <Wire card={card} land={land} runKey={runKey} />
        <div className="ia-cell ia-cell-s" style={vars({ '--land': toServer ? `${land}s` : '0s' })}>
          <Server step={step} prev={prev} toServer={toServer} runKey={runKey} fwTick={fwTick} />
          {toServer && <span key={runKey} className="ia-ring" data-tone={card?.tone} />}
        </div>
      </div>

      <div className="border-t border-border px-4 pt-4 pb-3 sm:px-6">
        <div className="flex items-start gap-4">
          <span className="pt-0.5 font-mono text-xs text-muted-foreground tabular-nums">
            {String(stepIndex + 1).padStart(2, '0')}
            <span className="opacity-50">/{String(current.steps.length).padStart(2, '0')}</span>
          </span>
          <div key={runKey} className="min-h-[5.25rem] min-w-0 flex-1 sm:min-h-[4.5rem]" aria-live={playing ? 'off' : 'polite'}>
            <p className="ia-cap font-semibold tracking-tight">{step.title}</p>
            <p className="ia-cap mt-1 text-sm leading-6 text-muted-foreground" style={{ animationDelay: '70ms' }}>
              {rich(step.text)}
            </p>
          </div>
        </div>
        <div className="mt-2 flex items-center gap-3">
          <ol className="flex flex-1 gap-1.5" aria-label="Steps">
            {current.steps.map((st, i) => (
              <li key={i} className="flex-1">
                <button type="button" onClick={() => go(scene, i)} aria-label={`Step ${i + 1}: ${st.title}`} aria-current={i === stepIndex ? 'step' : undefined} className="ia-seg group">
                  <span>
                    <i ref={i === stepIndex ? fill : undefined} style={vars({ '--p': i < stepIndex ? 1 : 0 })} />
                  </span>
                </button>
              </li>
            ))}
          </ol>
          <div className="flex shrink-0 items-center gap-0.5">
            <button type="button" onClick={back} aria-label="Previous step" className="ia-ctl">
              <ChevronLeft className="size-4" />
            </button>
            <button type="button" onClick={() => setPlaying((p) => !p)} aria-label={playing ? 'Pause the walkthrough' : 'Play the walkthrough'} className="ia-ctl">
              {playing ? <Pause className="size-3.5" /> : <Play className="size-3.5" />}
            </button>
            <button type="button" onClick={next} aria-label="Next step" className="ia-ctl">
              <ChevronRight className="size-4" />
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
