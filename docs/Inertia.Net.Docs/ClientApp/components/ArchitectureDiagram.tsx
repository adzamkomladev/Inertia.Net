import { Cpu, Database, Pause, Play } from 'lucide-react'
import { useEffect, useId, useRef, useState, type CSSProperties } from 'react'
import dotnet from '../assets/logos/dotnet.svg'
import { cn } from '../lib/utils'
import { ReactLogo, SvelteLogo, VueLogo } from './icons/FrameworkLogos'

type Part = 'browser' | 'page' | 'client' | 'pipe' | 'adapter' | 'handler' | 'vite' | 'ssr' | 'db' | 'live'
type PathKey = 'req' | 'res' | 'dbReq' | 'dbRes' | 'ssrReq' | 'ssrRes' | 'live'
type Tone = 'req' | 'res' | 'live'
type PageState = 'empty' | 'loading' | 'ready'

type Packet = { path: PathKey; label: string; tone: Tone; delay?: number }
type Step = {
  title: string
  text: string
  /** Parts that light up while the step plays. */
  on: Part[]
  /** Parts shown dimmed because this request never touches them. */
  off?: Part[]
  packets?: Packet[]
  url: string
  page: { name: string; state: PageState }
  /** What is on the wire right now, shown under the protocol pipe. At most 18 characters. */
  wire?: string
  /** A short note on the cost of the payload, shown under the wire chip. At most 20 characters. */
  stat?: string
  /** Show a cursor gliding to the first row and tapping it. */
  cursor?: boolean
  ms?: number
}
type Scene = { id: string; label: string; steps: Step[] }

const scenes: Scene[] = [
  {
    id: 'first',
    label: 'First visit',
    steps: [
      { title: 'The browser asks for /users', text: 'A normal navigation. Nothing is loaded yet, so the page area is empty.', on: ['browser', 'pipe'], packets: [{ path: 'req', label: 'GET /users', tone: 'req' }], url: '/users', page: { name: 'none yet', state: 'empty' }, wire: 'Accept: text/html' },
      { title: 'Your endpoint runs', text: 'An ordinary Minimal API, controller or FastEndpoint. It loads data and returns Render("Users/Index", props).', on: ['handler', 'db'], packets: [{ path: 'dbReq', label: 'query', tone: 'req' }, { path: 'dbRes', label: '25 users', tone: 'res', delay: 1.5 }], url: '/users', page: { name: 'none yet', state: 'empty' }, ms: 4400 },
      { title: 'Inertia.Net builds the page object', text: 'The adapter wraps the result as one page object: the component name, its props, the URL and the asset version.', on: ['adapter'], url: '/users', page: { name: 'none yet', state: 'empty' }, wire: '{ page object }' },
      { title: 'The SSR server renders it', text: 'On the first visit only, the page object goes to Bun or Node, which renders your real React, Vue or Svelte component to HTML.', on: ['adapter', 'ssr'], packets: [{ path: 'ssrReq', label: 'page object', tone: 'req' }, { path: 'ssrRes', label: 'HTML', tone: 'res', delay: 1.5 }], url: '/users', page: { name: 'none yet', state: 'empty' }, ms: 4400 },
      { title: 'HTML comes back with the data embedded', text: 'The browser gets complete markup it can show at once, with the page object tucked into a data-page attribute.', on: ['adapter', 'pipe', 'browser'], packets: [{ path: 'res', label: 'HTML + data-page', tone: 'res' }], url: '/users', page: { name: 'Users/Index', state: 'ready' }, wire: 'data-page="{…}"', stat: 'full HTML document' },
      { title: 'The Inertia client takes over', text: 'It boots from the embedded page object, hydrates the component and starts handling links and forms. No second fetch is needed.', on: ['client', 'page'], url: '/users', page: { name: 'Users/Index', state: 'ready' } },
    ],
  },
  {
    id: 'nav',
    label: 'Click a link',
    steps: [
      { title: 'A link is clicked', text: 'The Inertia client intercepts the click, so the browser does not reload the page.', cursor: true, ms: 4600, on: ['client', 'page'], url: '/users', page: { name: 'Users/Index', state: 'ready' } },
      { title: 'It sends a request with X-Inertia', text: 'The header tells the server the caller is the Inertia client, which wants JSON rather than a whole document.', on: ['client', 'pipe'], packets: [{ path: 'req', label: 'GET /users/1', tone: 'req' }], url: '/users/1', page: { name: 'Users/Index', state: 'ready' }, wire: 'X-Inertia: true' },
      { title: 'The same endpoint runs', text: 'The route and the data access are exactly as on the first visit. The adapter sees the header and skips the HTML shell and the SSR server.', on: ['handler', 'db', 'adapter'], off: ['ssr'], packets: [{ path: 'dbReq', label: 'query', tone: 'req' }, { path: 'dbRes', label: 'user 1', tone: 'res', delay: 1.5 }], url: '/users/1', page: { name: 'Users/Index', state: 'ready' }, ms: 4400 },
      { title: 'Only JSON goes back', text: 'The response is the page object and nothing else, a few kilobytes instead of a full page.', on: ['adapter', 'pipe'], off: ['ssr'], packets: [{ path: 'res', label: 'JSON page object', tone: 'res' }], url: '/users/1', page: { name: 'Users/Index', state: 'ready' }, wire: '{ page object }', stat: 'JSON only, no shell' },
      { title: 'The client swaps the component', text: 'It renders Users/Show with the new props and updates the URL and history. The page never reloaded.', on: ['client', 'page'], off: ['ssr'], url: '/users/1', page: { name: 'Users/Show', state: 'ready' } },
    ],
  },
  {
    id: 'defer',
    label: 'Deferred props',
    steps: [
      { title: 'A dashboard with a slow query', text: 'The visitor opens /dashboard. One prop, the stats, takes a while to compute.', on: ['browser', 'pipe'], packets: [{ path: 'req', label: 'GET /dashboard', tone: 'req' }], url: '/dashboard', page: { name: 'none yet', state: 'empty' }, wire: 'Accept: text/html' },
      { title: 'The slow prop is left out', text: 'Defer(...) props are not computed for the first response. The adapter lists their names under deferredProps and answers straight away.', on: ['handler', 'adapter'], url: '/dashboard', page: { name: 'none yet', state: 'empty' }, wire: 'deferredProps' },
      { title: 'The page shows up at once', text: 'The visitor sees the page, with placeholders where the stats will go.', on: ['adapter', 'pipe', 'browser', 'page'], packets: [{ path: 'res', label: 'HTML + user', tone: 'res' }], url: '/dashboard', page: { name: 'Dashboard', state: 'loading' }, wire: 'data-page="{…}"', stat: 'stats left out' },
      { title: 'The client asks for the rest', text: 'As soon as the page mounts it sends a partial reload for just the deferred props. The page stays interactive meanwhile.', on: ['client', 'pipe'], packets: [{ path: 'req', label: 'reload: stats', tone: 'req' }], url: '/dashboard', page: { name: 'Dashboard', state: 'loading' }, wire: 'only: stats' },
      { title: 'The slow query runs', text: 'Only the stats loader runs now, so nothing else is recomputed.', on: ['handler', 'db'], packets: [{ path: 'dbReq', label: 'big query', tone: 'req' }, { path: 'dbRes', label: 'stats', tone: 'res', delay: 2.2 }], url: '/dashboard', page: { name: 'Dashboard', state: 'loading' }, ms: 5200 },
      { title: 'The placeholders fill in', text: 'The stats arrive as JSON and the component re-renders with real data.', on: ['adapter', 'pipe', 'client', 'page'], packets: [{ path: 'res', label: 'stats', tone: 'res' }], url: '/dashboard', page: { name: 'Dashboard', state: 'ready' }, wire: '{ stats }', stat: 'just the stats' },
    ],
  },
  {
    id: 'live',
    label: 'Live props',
    steps: [
      { title: 'Something changes on the server', text: 'Your code saves an order and publishes a live event through IInertiaLive. The open dashboard is not polling.', on: ['handler'], url: '/dashboard', page: { name: 'Dashboard', state: 'ready' } },
      { title: 'The event reaches the browser', text: 'The transport (SignalR, or your own) delivers it to every page whose liveProps name that event.', on: ['live', 'adapter', 'client'], packets: [{ path: 'live', label: 'orders.changed', tone: 'live' }], url: '/dashboard', page: { name: 'Dashboard', state: 'ready' } },
      { title: 'The client reloads just that prop', text: 'It sends a partial reload for the stats, the prop listening to the event.', on: ['client', 'pipe'], packets: [{ path: 'req', label: 'reload: stats', tone: 'req' }], url: '/dashboard', page: { name: 'Dashboard', state: 'ready' }, wire: 'only: stats' },
      { title: 'Fresh data comes back', text: 'The adapter answers with only the stats. The rest of the page is untouched.', on: ['handler', 'adapter', 'pipe'], packets: [{ path: 'res', label: 'stats', tone: 'res' }], url: '/dashboard', page: { name: 'Dashboard', state: 'ready' }, wire: '{ stats }', stat: 'just the stats' },
      { title: 'The numbers update', text: 'The component re-renders in place. No component subscribed to anything, and nothing was polled.', on: ['client', 'page'], url: '/dashboard', page: { name: 'Dashboard', state: 'ready' } },
    ],
  },
]

type R = { x: number; y: number; w: number; h: number }
type Layout = {
  w: number
  h: number
  browser: R
  app: R
  ssr: R
  db: R
  pipe: R
  wire: { x: number; y: number }
  chips: Record<'page' | 'client' | 'adapter' | 'handler' | 'vite', R>
  paths: Record<PathKey, string>
  /** Where the static live-path caption goes. */
  liveLabel: { x: number; y: number }
  compact: boolean
}

const wide: Layout = {
  w: 1000,
  h: 464,
  browser: { x: 20, y: 40, w: 230, h: 360 },
  app: { x: 400, y: 40, w: 240, h: 360 },
  ssr: { x: 760, y: 240, w: 220, h: 160 },
  db: { x: 760, y: 40, w: 220, h: 160 },
  pipe: { x: 256, y: 130, w: 140, h: 184 },
  wire: { x: 326, y: 336 },
  chips: {
    page: { x: 36, y: 92, w: 198, h: 170 },
    client: { x: 36, y: 288, w: 198, h: 96 },
    adapter: { x: 416, y: 196, w: 208, h: 116 },
    handler: { x: 416, y: 92, w: 208, h: 84 },
    vite: { x: 416, y: 330, w: 208, h: 54 },
  },
  paths: {
    req: 'M250 196 H400',
    res: 'M400 258 H250',
    dbReq: 'M640 118 H760',
    dbRes: 'M760 148 H640',
    ssrReq: 'M640 270 H760',
    ssrRes: 'M760 296 H640',
    live: 'M520 400 V436 H135 V400',
  },
  liveLabel: { x: 330, y: 454 },
  compact: false,
}

const tall: Layout = {
  w: 446,
  h: 950,
  browser: { x: 20, y: 10, w: 370, h: 246 },
  app: { x: 20, y: 400, w: 370, h: 320 },
  ssr: { x: 215, y: 800, w: 175, h: 140 },
  db: { x: 20, y: 800, w: 175, h: 140 },
  pipe: { x: 20, y: 270, w: 370, h: 116 },
  wire: { x: 210, y: 346 },
  chips: {
    page: { x: 36, y: 58, w: 176, h: 186 },
    client: { x: 222, y: 58, w: 152, h: 186 },
    adapter: { x: 36, y: 448, w: 338, h: 124 },
    handler: { x: 36, y: 584, w: 338, h: 68 },
    vite: { x: 36, y: 664, w: 338, h: 44 },
  },
  paths: {
    req: 'M130 256 V400',
    res: 'M290 400 V256',
    dbReq: 'M60 720 V800',
    dbRes: 'M135 800 V720',
    ssrReq: 'M265 720 V800',
    ssrRes: 'M340 800 V720',
    live: 'M390 520 H414 V150 H390',
  },
  liveLabel: { x: 432, y: 340 },
  compact: true,
}

const tone: Record<Tone, string> = { req: 'var(--primary)', res: 'var(--success)', live: 'var(--warning)' }
const pathTone: Record<PathKey, Tone> = { req: 'req', res: 'res', dbReq: 'req', dbRes: 'res', ssrReq: 'req', ssrRes: 'res', live: 'live' }

function Chip({ r, title, sub, glue, on, off, children, compact, hit }: { r: R; title: string; sub?: string; glue?: boolean; on?: boolean; off?: boolean; children?: React.ReactNode; compact?: boolean; hit?: React.SVGProps<SVGGElement> }) {
  return (
    <g className={cn('arch-chip', on && 'arch-on', off && 'arch-off')} {...hit}>
      <rect
        x={r.x}
        y={r.y}
        width={r.w}
        height={r.h}
        rx="14"
        fill={glue ? 'var(--glue-fill)' : 'var(--card)'}
        stroke={on ? 'var(--primary)' : glue ? 'var(--glue-stroke)' : 'var(--border-strong)'}
        strokeWidth={on ? 2 : 1.25}
      />
      <text x={r.x + 14} y={r.y + (compact ? 27 : 25)} className="fill-foreground font-sans text-[14px] font-semibold">
        {title}
      </text>
      {sub && (
        <text x={r.x + 14} y={r.y + (compact ? 48 : 44)} className="fill-muted-foreground font-sans text-[12px]">
          {sub}
        </text>
      )}
      {glue && !compact && (
        <g>
          <rect x={r.x + r.w - 54} y={r.y + 10} width="40" height="18" rx="9" fill="var(--primary)" opacity="0.14" />
          <text x={r.x + r.w - 34} y={r.y + 23} textAnchor="middle" className="fill-primary font-sans text-[11px] font-semibold">
            glue
          </text>
        </g>
      )}
      {children}
    </g>
  )
}

function Frame({ r, off, children }: { r: R; off?: boolean; children?: React.ReactNode }) {
  return (
    <g className={cn('arch-frame', off && 'arch-off')}>
      <rect x={r.x} y={r.y} width={r.w} height={r.h} rx="20" fill="color-mix(in oklab, var(--muted) 55%, var(--card))" stroke="var(--border)" />
      {children}
    </g>
  )
}

const tips: Partial<Record<Part, { title: string; text: string }>> = {
  page: { title: 'Page component', text: 'Your React, Vue or Svelte component. It receives the props as ordinary props.' },
  client: { title: 'Inertia client', text: 'Intercepts links and forms, sends X-Inertia requests, swaps pages and manages history.' },
  pipe: { title: 'Inertia protocol', text: 'One X-Inertia header and a JSON page object. That is the whole contract between the two sides.' },
  handler: { title: 'Your endpoint', text: 'A Minimal API, controller or FastEndpoint. Routing, auth and data access stay in C#.' },
  adapter: { title: 'Inertia.Net', text: 'Turns Render(...) into a page object, then answers with JSON, HTML or a forced reload.' },
  vite: { title: 'Root template', text: 'The HTML shell that holds the page object and the Vite script and style tags.' },
  ssr: { title: 'SSR server', text: 'Bun or Node, started and watched by the app. It renders your real components to HTML.' },
  db: { title: 'Your data', text: 'EF Core, Dapper, a cache: whatever your endpoints already use. Inertia never touches it.' },
}

/** The last point of a path built from M, H and V commands. */
function endPoint(d: string) {
  let x = 0
  let y = 0
  for (const m of d.matchAll(/([MHV])\s*(-?[\d.]+)(?:[\s,]+(-?[\d.]+))?/g)) {
    const v = Number(m[2])
    if (m[1] === 'M') {
      x = v
      y = Number(m[3])
    } else if (m[1] === 'H') x = v
    else y = v
  }
  return { x, y }
}

const avatar = 'color-mix(in oklab, var(--primary) 32%, var(--card))'
const barFill = (state: PageState) => (state === 'ready' ? 'color-mix(in oklab, var(--muted-foreground) 36%, var(--card))' : 'color-mix(in oklab, var(--muted-foreground) 14%, var(--card))')

/** A tiny stand-in for the page, so the browser visibly fills in as data arrives. */
function PageMock({ r, name, state, link }: { r: R; name: string; state: PageState; link?: boolean }) {
  const x = r.x + 14
  const w = r.w - 28
  const y0 = r.y + 64
  const kind = name.startsWith('Dashboard') ? 'dash' : name === 'Users/Show' ? 'detail' : 'list'
  const empty = state === 'empty'
  const common = (i: number) => ({
    className: cn(state === 'loading' && 'arch-shimmer', state === 'ready' && 'arch-rise'),
    style: { animationDelay: state === 'loading' ? `${i * 0.18}s` : `${i * 70}ms` } as CSSProperties,
    fill: barFill(state),
    stroke: empty ? 'var(--border-strong)' : 'none',
    strokeDasharray: empty ? '3 3' : undefined,
  })

  if (kind === 'dash') {
    const tile = (w - 8) / 2
    return (
      <g key={`${name}-${state}`}>
        {[0, 1].map((i) => (
          <g key={i}>
            <rect x={x + i * (tile + 8)} y={y0} width={tile} height="34" rx="8" {...common(i)} />
            {state === 'ready' && <rect x={x + i * (tile + 8) + 8} y={y0 + 10} width={tile * 0.5} height="9" rx="4.5" fill="var(--success)" opacity="0.8" className="arch-rise" style={{ animationDelay: `${i * 70 + 120}ms` }} />}
          </g>
        ))}
        {[0.45, 0.7, 0.55, 0.9, 0.65].map((h, i) => (
          <rect key={i} x={x + i * ((w - 10) / 5 + 2.5)} y={y0 + 44 + (1 - (state === 'ready' ? h : 0.35)) * 34} width={(w - 10) / 5 - 2} height={(state === 'ready' ? h : 0.35) * 34} rx="3" {...common(i + 2)} />
        ))}
      </g>
    )
  }

  if (kind === 'detail') {
    return (
      <g key={`${name}-${state}`}>
        <circle cx={x + 16} cy={y0 + 16} r="15" fill={empty ? 'none' : avatar} stroke={empty ? 'var(--border-strong)' : 'none'} strokeDasharray="3 3" className={cn(state === 'ready' && 'arch-rise')} />
        <rect x={x + 40} y={y0 + 6} width={w * 0.5} height="10" rx="5" {...common(1)} />
        <rect x={x + 40} y={y0 + 22} width={w * 0.34} height="8" rx="4" {...common(2)} />
        <rect x={x} y={y0 + 48} width={w * 0.9} height="9" rx="4.5" {...common(3)} />
        <rect x={x} y={y0 + 66} width={w * 0.7} height="9" rx="4.5" {...common(4)} />
      </g>
    )
  }

  return (
    <g key={`${name}-${state}`}>
      {[0, 1, 2].map((i) => {
        const y = y0 + i * 27
        const hl = link && i === 0
        return (
          <g key={i}>
            {hl && <rect x={x - 6} y={y - 3} width={w + 12} height="26" rx="8" fill="var(--primary)" className="arch-hl" />}
            <circle cx={x + 10} cy={y + 10} r="8.5" fill={empty ? 'none' : avatar} stroke={empty ? 'var(--border-strong)' : 'none'} strokeDasharray="3 3" className={cn(state === 'loading' && 'arch-shimmer', state === 'ready' && 'arch-rise')} style={{ animationDelay: `${i * 70}ms` }} />
            <rect x={x + 26} y={y + 3} width={w * (hl ? 0.46 : [0.52, 0.42, 0.48][i])} height="9" rx="4.5" {...common(i)} fill={hl ? 'var(--primary)' : barFill(state)} />
            <rect x={x + 26} y={y + 16} width={w * [0.3, 0.26, 0.34][i]} height="6" rx="3" {...common(i)} opacity="0.7" />
          </g>
        )
      })}
    </g>
  )
}

type SceneProps = {
  L: Layout
  step: Step
  uid: string
  runKey: string
  desc: string
  ambient: boolean
  tip: Part | null
  onTip: (p: Part | null) => void
  onJump: (p: Part) => void
}

function Scene({ L, step, uid, runKey, desc, ambient, tip, onTip, onJump }: SceneProps) {
  const on = (p: Part) => step.on.includes(p)
  const off = (p: Part) => !!step.off?.includes(p)
  const { chips } = L
  const usedPaths = new Map((step.packets ?? []).map((p) => [p.path, p.delay ?? 0]))
  const c = L.compact
  const sideLane = (p: Packet) => c && (p.path === 'req' || p.path === 'res' || p.path.startsWith('db') || p.path.startsWith('ssr'))
  const loading = !!step.packets?.some((p) => p.path === 'req')
  const urlPill = { x: L.browser.x + 62, y: L.browser.y + 10, w: L.browser.w - 78 }

  // Hover and click wiring. Touch has no hover, so tooltips only follow a mouse.
  const hit = (p: Part) => ({
    onPointerEnter: (e: React.PointerEvent) => e.pointerType === 'mouse' && onTip(p),
    onPointerLeave: () => onTip(null),
    onClick: () => onJump(p),
  })
  const rects: Record<string, R> = { page: chips.page, client: chips.client, adapter: chips.adapter, handler: chips.handler, vite: chips.vite, ssr: L.ssr, db: L.db, pipe: L.pipe }
  const tipRect = tip ? rects[tip] : undefined
  const tipText = tip ? tips[tip] : undefined
  const tw = 236
  const tx = tipRect ? Math.min(Math.max(tipRect.x + tipRect.w / 2 - tw / 2, 6), L.w - tw - 6) : 0
  const above = tipRect ? tipRect.y > 110 : true
  const ty = tipRect ? (above ? tipRect.y - 92 : tipRect.y + tipRect.h + 8) : 0

  return (
    <svg viewBox={`0 0 ${L.w} ${L.h}`} className={cn('block h-auto w-full', c && 'arch-tall', ambient && 'arch-ambient')} role="img" aria-label={`Architecture of an Inertia.Net app: a browser running React, Vue or Svelte with the Inertia client, an ASP.NET Core app with your endpoints and the Inertia.Net adapter, a Bun or Node SSR server and a database, joined by the Inertia protocol. ${desc}`}>
      <defs>
        {(['req', 'res', 'live'] as Tone[]).map((t) => (
          <marker key={t} id={`${uid}-${t}`} viewBox="0 0 10 10" refX="8.5" refY="5" markerWidth="7" markerHeight="7" orient="auto">
            <path d="M1.5 1.5 8.5 5 1.5 8.5" fill="none" stroke={tone[t]} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
          </marker>
        ))}
        <pattern id={`${uid}-dots`} width="20" height="20" patternUnits="userSpaceOnUse">
          <circle cx="1" cy="1" r="1" fill="var(--border)" />
        </pattern>
      </defs>
      <rect width={L.w} height={L.h} fill={`url(#${uid}-dots)`} opacity="0.55" />

      {/* The Inertia protocol: the glue between the two apps. */}
      <g className={cn('arch-chip', on('pipe') && 'arch-on')} {...hit('pipe')}>
        <rect x={L.pipe.x} y={L.pipe.y} width={L.pipe.w} height={L.pipe.h} rx="16" fill="var(--glue-fill)" stroke={on('pipe') ? 'var(--primary)' : 'var(--glue-stroke)'} strokeWidth={on('pipe') ? 2 : 1.25} strokeDasharray="5 4" />
        <text x={L.pipe.x + L.pipe.w / 2} y={L.pipe.y + (c ? 30 : 22)} textAnchor="middle" className="fill-primary font-sans text-[13px] font-semibold">
          Inertia protocol
        </text>
        {!c && (
          <text x={L.pipe.x + L.pipe.w / 2} y={L.pipe.y + L.pipe.h - 16} textAnchor="middle" className="fill-muted-foreground font-sans text-[12px]">
            JSON page object
          </text>
        )}
      </g>

      {/* Lanes sit under the boxes so the arrows end exactly at their edges. Idle lanes drift; hot lanes draw themselves in. */}
      {(Object.keys(L.paths) as PathKey[]).map((k) => {
        const hot = usedPaths.has(k) || (k === 'live' && on('live'))
        const t = pathTone[k]
        const dim = (k === 'ssrReq' || k === 'ssrRes') && off('ssr')
        return (
          <g key={k} opacity={dim ? 0.35 : 1}>
            <path
              d={L.paths[k]}
              fill="none"
              stroke={hot ? tone[t] : 'var(--border-strong)'}
              strokeWidth={hot ? 2 : 1.5}
              strokeDasharray={k === 'live' ? '2 7' : '3 8'}
              strokeLinecap="round"
              markerEnd={`url(#${uid}-${hot ? t : 'req'})`}
              className="arch-lane"
              style={{ transition: 'stroke 300ms, stroke-width 300ms' }}
            />
            {hot && (
              <path
                key={`${runKey}-${k}`}
                d={L.paths[k]}
                fill="none"
                pathLength={1}
                stroke={tone[t]}
                strokeWidth="2.5"
                strokeLinecap="round"
                className="arch-draw"
                style={{ '--delay': `${usedPaths.get(k) ?? 0}s` } as CSSProperties}
              />
            )}
          </g>
        )
      })}
      <text x={L.liveLabel.x} y={L.liveLabel.y} textAnchor="middle" className="fill-muted-foreground font-sans text-[12px]" transform={c ? `rotate(90 ${L.liveLabel.x} ${L.liveLabel.y})` : undefined} dy={c ? -6 : 0}>
        live props · SignalR
      </text>

      {step.wire && (
        <g key={`${runKey}-wire`} className="arch-fade">
          <rect x={L.wire.x - 74} y={L.wire.y - 14} width="148" height="28" rx="8" fill="var(--code-bg)" stroke="var(--border-strong)" />
          <text x={L.wire.x} y={L.wire.y + 4} textAnchor="middle" className="fill-foreground font-mono text-[11px]">
            {step.wire}
          </text>
        </g>
      )}
      {step.stat && (
        <g key={`${runKey}-stat`} className="arch-fade" style={{ animationDelay: '1.5s' }}>
          <rect x={L.wire.x - 74} y={L.wire.y + 20} width="148" height="22" rx="11" fill="var(--glue-fill)" stroke="var(--glue-stroke)" />
          <text x={L.wire.x} y={L.wire.y + 35} textAnchor="middle" className="fill-primary font-sans text-[11px] font-semibold">
            {step.stat}
          </text>
        </g>
      )}

      {/* Browser */}
      <Frame r={L.browser} off={off('browser')}>
        <circle cx={L.browser.x + 20} cy={L.browser.y + 22} r="3.5" fill="var(--border-strong)" />
        <circle cx={L.browser.x + 33} cy={L.browser.y + 22} r="3.5" fill="var(--border-strong)" />
        <circle cx={L.browser.x + 46} cy={L.browser.y + 22} r="3.5" fill="var(--border-strong)" />
        <rect x={urlPill.x} y={urlPill.y} width={urlPill.w} height="24" rx="12" fill="var(--card)" stroke={on('browser') ? 'var(--primary)' : 'var(--border)'} />
        {loading && <rect key={`${runKey}-load`} x={urlPill.x + 12} y={urlPill.y + 21} width={urlPill.w - 24} height="2" rx="1" fill="var(--primary)" className="arch-load" />}
        <text key={`${runKey}-url`} x={urlPill.x + 14} y={urlPill.y + 16.5} className="arch-fade fill-foreground font-mono text-[12px]">
          {step.url}
        </text>
      </Frame>
      <Chip r={chips.page} title="Page component" sub={step.page.name} on={on('page') || (on('browser') && step.page.state === 'ready')} compact={c} hit={hit('page')}>
        <PageMock r={chips.page} name={step.page.name} state={step.page.state} link={step.cursor} />
        {step.page.state === 'empty' && (
          <text x={chips.page.x + 14} y={chips.page.y + chips.page.h - 14} className="fill-muted-foreground font-sans text-[12px]">
            waiting for the server
          </text>
        )}
      </Chip>
      <Chip r={chips.client} title="Inertia client" sub={c ? 'router, forms' : 'router · forms · history'} glue on={on('client')} compact={c} hit={hit('client')}>
        <g transform={`translate(${chips.client.x + 14} ${chips.client.y + chips.client.h - 34})`}>
          <ReactLogo width="20" height="20" />
          <VueLogo x="28" width="20" height="20" />
          <SvelteLogo x="56" width="16" height="20" />
        </g>
      </Chip>

      {/* ASP.NET Core */}
      <Frame r={L.app}>
        <rect x={L.app.x + 12} y={L.app.y + 11} width="30" height="28" rx="7" fill="#fff" stroke="var(--border)" />
        <image href={dotnet} x={L.app.x + 13} y={L.app.y + 13} width="28" height="24" />
        <text x={L.app.x + 48} y={L.app.y + 31} className="fill-foreground font-sans text-[15px] font-semibold">
          ASP.NET Core
        </text>
      </Frame>
      <Chip r={chips.handler} title="Your endpoint" sub="routes, auth, validation" on={on('handler')} compact={c} hit={hit('handler')} />
      <Chip r={chips.adapter} title="Inertia.Net" sub="Render, props, versioning" glue on={on('adapter')} compact={c} hit={hit('adapter')}>
        <text x={chips.adapter.x + 14} y={chips.adapter.y + (c ? 76 : 63)} className="fill-muted-foreground font-sans text-[12px]">
          shared data, validation, SSR
        </text>
        <text x={chips.adapter.x + 14} y={chips.adapter.y + (c ? 98 : 82)} className="fill-muted-foreground font-sans text-[12px]">
          deferred and live props
        </text>
      </Chip>
      <Chip r={chips.vite} title="Root template" sub={c ? undefined : 'Vite manifest, asset tags'} on={on('vite')} compact={c} hit={hit('vite')} />

      {/* Side services */}
      <Frame r={L.ssr} off={off('ssr')}>
        <g className={cn('arch-chip', on('ssr') && 'arch-on')} {...hit('ssr')}>
          <rect x={L.ssr.x} y={L.ssr.y} width={L.ssr.w} height={L.ssr.h} rx="20" fill="transparent" />
          <Cpu x={L.ssr.x + 16} y={L.ssr.y + 14} width={24} height={24} stroke={on('ssr') ? 'var(--primary)' : 'var(--muted-foreground)'} strokeWidth={1.8} className={cn(on('ssr') && 'arch-spin')} />
          <text x={L.ssr.x + 50} y={L.ssr.y + 32} className="fill-foreground font-sans text-[15px] font-semibold">
            SSR server
          </text>
          <text x={L.ssr.x + 16} y={L.ssr.y + 66} className="fill-muted-foreground font-sans text-[12px]">
            Bun or Node, run by the app
          </text>
          <text x={L.ssr.x + 16} y={L.ssr.y + 86} className="fill-muted-foreground font-sans text-[12px]">
            Renders your real components
          </text>
        </g>
        {step.off?.includes('ssr') && (
          <text x={L.ssr.x + 16} y={L.ssr.y + L.ssr.h - 16} className="fill-muted-foreground font-sans text-[12px] font-medium">
            not needed for this request
          </text>
        )}
      </Frame>
      <Frame r={L.db}>
        <g className={cn('arch-chip', on('db') && 'arch-on')} {...hit('db')}>
          <rect x={L.db.x} y={L.db.y} width={L.db.w} height={L.db.h} rx="20" fill="transparent" />
          <Database x={L.db.x + 16} y={L.db.y + 14} width={24} height={24} stroke={on('db') ? 'var(--primary)' : 'var(--muted-foreground)'} strokeWidth={1.8} />
          <text x={L.db.x + 50} y={L.db.y + 32} className="fill-foreground font-sans text-[15px] font-semibold">
            Your data
          </text>
          <text x={L.db.x + 16} y={L.db.y + 66} className="fill-muted-foreground font-sans text-[12px]">
            EF Core, SQL, cache
          </text>
        </g>
      </Frame>

      {/* A click: the cursor glides to the first row, taps it, and the row lights up. */}
      {step.cursor && (
        <g key={`${runKey}-cursor`}>
          <g
            className="arch-cursor"
            style={{ '--x0': `${chips.page.x + chips.page.w - 22}px`, '--y0': `${chips.page.y + chips.page.h - 22}px`, '--x1': `${chips.page.x + 52}px`, '--y1': `${chips.page.y + 64 + 8}px` } as CSSProperties}
          >
            <path d="M0 0 0 16 4.2 12.2 7 18.5 9.6 17.4 6.9 11.2 12.4 11 Z" fill="#fff" stroke="#111" strokeWidth="1.2" strokeLinejoin="round" />
          </g>
          <circle cx={chips.page.x + 52} cy={chips.page.y + 72} r="7" fill="none" stroke="var(--primary)" strokeWidth="2" className="arch-ripple" style={{ '--delay': '1.4s' } as CSSProperties} />
        </g>
      )}

      {/* Packets travel along the lanes with a short comet tail, and a ring where they land. */}
      {(step.packets ?? []).map((p, i) => {
        const w = p.label.length * (c ? 7.3 : 6.4) + 18
        const side = sideLane(p)
        const delay = p.delay ?? 0
        const end = endPoint(L.paths[p.path])
        return (
          <g key={`${runKey}-${i}`}>
            {[3, 2, 1].map((k) => (
              <g key={k} className="arch-packet" style={{ '--path': `path("${L.paths[p.path]}")`, '--delay': `${delay + k * 0.07}s` } as CSSProperties}>
                <circle r={5 - k * 0.9} fill={tone[p.tone]} opacity={0.42 - k * 0.1} />
              </g>
            ))}
            <g className="arch-packet" style={{ '--path': `path("${L.paths[p.path]}")`, '--delay': `${delay}s` } as CSSProperties}>
              <circle r="11" fill={tone[p.tone]} opacity="0.22" />
              <circle r="5" fill={tone[p.tone]} />
              <g transform={side ? 'translate(14 -2)' : 'translate(0 -24)'}>
                <rect x={side ? 0 : -w / 2} y="-11" width={w} height="22" rx="11" fill="var(--card)" stroke={tone[p.tone]} />
                <text x={side ? w / 2 : 0} y="4" textAnchor="middle" className="fill-foreground font-sans text-[11.5px] font-medium">
                  {p.label}
                </text>
              </g>
            </g>
            <circle cx={end.x} cy={end.y} r="6" fill="none" stroke={tone[p.tone]} strokeWidth="2" className="arch-ripple" style={{ '--delay': `${delay + 1.7}s` } as CSSProperties} />
          </g>
        )
      })}

      {/* Hovering a part explains it. */}
      {tipRect && tipText && (
        <foreignObject x={tx} y={ty} width={tw} height="84" className="pointer-events-none overflow-visible">
          <div className="arch-fade rounded-xl border border-border bg-popover px-3 py-2 text-[12px] leading-[1.4] text-popover-foreground shadow-[var(--shadow-float)]">
            <p className="font-semibold">{tipText.title}</p>
            <p className="mt-0.5 text-muted-foreground">{tipText.text}</p>
          </div>
        </foreignObject>
      )}
    </svg>
  )
}

/** The landing-page hero diagram: a live walkthrough of how a request moves through an Inertia.Net app. */
export function ArchitectureDiagram() {
  const uid = useId().replace(/:/g, '')
  const ref = useRef<HTMLDivElement>(null)
  const [scene, setScene] = useState(0)
  const [stepIndex, setStep] = useState(0)
  const [playing, setPlaying] = useState(true)
  const [inView, setInView] = useState(false)
  const [hold, setHold] = useState(false)
  const [tip, setTip] = useState<Part | null>(null)

  const current = scenes[scene]
  const step = current.steps[stepIndex]
  const runKey = `${scene}-${stepIndex}`
  const desc = `${current.label}, step ${stepIndex + 1} of ${current.steps.length}: ${step.title}. ${step.text}`

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) setPlaying(false)
    const el = ref.current
    if (!el) return
    const io = new IntersectionObserver(([e]) => setInView(e.isIntersecting), { threshold: 0.3 })
    io.observe(el)
    return () => io.disconnect()
  }, [])

  useEffect(() => {
    if (!playing || !inView || hold) return
    const last = stepIndex === current.steps.length - 1
    const t = setTimeout(
      () => {
        if (!last) setStep(stepIndex + 1)
        else {
          setScene((scene + 1) % scenes.length)
          setStep(0)
        }
      },
      (step.ms ?? 3800) + (last ? 1800 : 0),
    )
    return () => clearTimeout(t)
  }, [playing, inView, hold, scene, stepIndex, current.steps.length, step.ms])

  const jump = (part: Part) => {
    const i = current.steps.findIndex((st) => st.on.includes(part))
    if (i >= 0) pick(scene, i)
  }
  const ambient = playing && inView && !hold

  const pick = (s: number, i = 0) => {
    setScene(s)
    setStep(i)
  }

  return (
    <div
      ref={ref}
      className="rounded-2xl border border-border bg-card"
      onPointerEnter={() => setHold(true)}
      onPointerLeave={() => setHold(false)}
      onFocus={() => setHold(true)}
      onBlur={() => setHold(false)}
    >
      <div
        role="tablist"
        aria-label="Scenarios"
        className="flex flex-wrap items-center gap-1.5 border-b border-border p-2.5"
        onKeyDown={(e) => {
          const next = e.key === 'ArrowRight' ? scene + 1 : e.key === 'ArrowLeft' ? scene - 1 : e.key === 'Home' ? 0 : e.key === 'End' ? scenes.length - 1 : null
          if (next === null) return
          e.preventDefault()
          const n = (next + scenes.length) % scenes.length
          pick(n)
          document.getElementById(`${uid}-tab-${n}`)?.focus()
        }}
      >
        {scenes.map((s, i) => (
          <button
            key={s.id}
            id={`${uid}-tab-${i}`}
            type="button"
            role="tab"
            aria-selected={i === scene}
            aria-controls={`${uid}-panel`}
            tabIndex={i === scene ? 0 : -1}
            onClick={() => pick(i)}
            className={cn(
              'min-h-10 rounded-full px-4 py-2 text-sm font-medium transition-colors',
              i === scene ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent hover:text-foreground',
            )}
          >
            {s.label}
          </button>
        ))}
        <button
          type="button"
          onClick={() => setPlaying((p) => !p)}
          aria-label={playing ? 'Pause the animation' : 'Play the animation'}
          className="ml-auto inline-flex min-h-10 items-center gap-2 rounded-full border border-border px-3.5 text-sm text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
        >
          {playing ? <Pause className="size-3.5" /> : <Play className="size-3.5" />}
          {playing ? 'Pause' : 'Play'}
        </button>
      </div>

      <div className="h-0.5 bg-transparent" aria-hidden="true">
        {playing && inView && !hold && (
          <div key={runKey} className="arch-progress h-full origin-left bg-primary/60" style={{ animationDuration: `${(step.ms ?? 3800) + (stepIndex === current.steps.length - 1 ? 1800 : 0)}ms` }} />
        )}
      </div>

      <div className="flex flex-col">
      <div role="tabpanel" id={`${uid}-panel`} aria-labelledby={`${uid}-tab-${scene}`} className="order-2 p-2 sm:p-5 lg:order-1">
        <div className="hidden lg:block">
          <Scene L={wide} step={step} uid={`${uid}w`} runKey={`w${runKey}`} desc={desc} ambient={ambient} tip={tip} onTip={setTip} onJump={jump} />
        </div>
        <div className="mx-auto max-w-[520px] lg:hidden">
          <Scene L={tall} step={step} uid={`${uid}t`} runKey={`t${runKey}`} desc={desc} ambient={ambient} tip={tip} onTip={setTip} onJump={jump} />
        </div>
      </div>

      <div className="order-1 border-b border-border px-4 py-4 sm:px-6 lg:order-2 lg:border-b-0 lg:border-t">
        <div className="flex flex-col items-start gap-3 sm:flex-row sm:gap-4">
          <ol className="flex w-full shrink-0 flex-wrap gap-1.5 pt-1 sm:w-52" aria-label="Steps">
            {current.steps.map((_, i) => (
              <li key={i}>
                <button
                  type="button"
                  aria-label={`Step ${i + 1}`}
                  aria-current={i === stepIndex ? 'step' : undefined}
                  onClick={() => pick(scene, i)}
                  className={cn('grid size-7 place-items-center rounded-full font-mono text-xs transition-colors', i === stepIndex ? 'bg-primary text-primary-foreground' : i < stepIndex ? 'bg-[var(--primary-soft)] text-primary' : 'bg-muted text-muted-foreground hover:bg-accent')}
                >
                  {i + 1}
                </button>
              </li>
            ))}
          </ol>
          <div className="min-h-[5.75rem] min-w-0 flex-1" key={runKey} aria-live={playing ? 'off' : 'polite'}>
            <p className="arch-fade font-semibold">{step.title}</p>
            <p className="arch-fade mt-1 text-sm leading-6 text-muted-foreground">{step.text}</p>
          </div>
        </div>
        <p className="mt-4 flex flex-wrap items-center gap-x-5 gap-y-1.5 text-xs text-muted-foreground">
          <span className="inline-flex items-center gap-1.5"><i className="inline-block size-3 rounded-sm border" style={{ background: 'var(--glue-fill)', borderColor: 'var(--glue-stroke)' }} />Inertia glue</span>
          <span className="inline-flex items-center gap-1.5"><i className="inline-block size-2.5 rounded-sm border border-border-strong bg-card" />Your code</span>
          <span className="inline-flex items-center gap-1.5"><i className="inline-block size-2 rounded-full bg-primary" />request</span>
          <span className="inline-flex items-center gap-1.5"><i className="inline-block size-2 rounded-full bg-[var(--success)]" />response</span>
          <span className="inline-flex items-center gap-1.5"><i className="inline-block size-2 rounded-full bg-[var(--warning)]" />live event</span>
        </p>
      </div>
      </div>
    </div>
  )
}
