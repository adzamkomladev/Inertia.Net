import { Braces, Cpu, Database, Globe, Radio, Server } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import type { ComponentType, CSSProperties, ReactNode, SVGProps } from 'react'
import { BrandMark, brandOf } from './Brands'

const W = 760
const CHAR = 7.1 // average glyph width of the 13px UI font, used to size labels without measuring
const MONO = 6.9 // the same for the 11.5px mono font

type IconType = ComponentType<SVGProps<SVGSVGElement>>


/** Scrolls sideways and fades its right edge while more content is hidden, so a cut-off column reads as scrollable. */
export function ScrollArea({ className, children }: { className?: string; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null)
  const [more, setMore] = useState(false)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const check = () => setMore(el.scrollWidth - el.clientWidth - el.scrollLeft > 4)
    check()
    el.addEventListener('scroll', check, { passive: true })
    const ro = new ResizeObserver(check)
    ro.observe(el)
    return () => {
      el.removeEventListener('scroll', check)
      ro.disconnect()
    }
  }, [])
  const mask = 'linear-gradient(to right, #000 calc(100% - 2.5rem), transparent)'
  return (
    <div ref={ref} className={className} style={more ? { maskImage: mask, WebkitMaskImage: mask } : undefined}>
      {children}
    </div>
  )
}

/** True once the element has been on screen; diagrams use it to play their motion once when first seen. */
function useSeen<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const [seen, setSeen] = useState(false)
  useEffect(() => {
    const el = ref.current
    if (!el || seen) return
    const io = new IntersectionObserver(([e]) => e.isIntersecting && (setSeen(true), io.disconnect()), { threshold: 0.35 })
    io.observe(el)
    return () => io.disconnect()
  }, [seen])
  return [ref, seen] as const
}

const hint = 'px-4 pb-3 pt-1 text-xs text-muted-foreground sm:hidden'

function iconFor(name: string): IconType {
  if (/browser|client|user/i.test(name)) return Globe
  if (/ssr|node|bun|render/i.test(name)) return Cpu
  if (/handler|code|publish/i.test(name)) return Braces
  if (/db|sql|database|store/i.test(name)) return Database
  if (/signalr|live|hub|broadcast/i.test(name)) return Radio
  return Server
}

function wrap(text: string, max: number): string[] {
  const lines: string[] = []
  let line = ''
  for (const word of text.split(' ')) {
    if (line && (line + ' ' + word).length > max) {
      lines.push(line)
      line = word
    } else line = line ? line + ' ' + word : word
  }
  return [...lines, line]
}

export type SequenceMessage = {
  from: number
  to: number
  text: string
  /** Replies are drawn dashed and in the secondary hue. */
  reply?: boolean
  /** A monospace payload shown under the arrow, such as a header or a JSON fragment. */
  code?: string
  /** Starts a named phase band that runs until the next `phase`. */
  phase?: string
}

type Actor = string | { name: string; sub?: string }

/**
 * A sequence diagram drawn as SVG: actor cards, lifelines, numbered messages, payload chips, phase bands and a packet
 * that travels along each arrow. `to === from` draws a note on that actor. Needs about 40rem, so narrow screens scroll.
 */
export function Sequence({ actors, messages, caption }: { actors: Actor[]; messages: SequenceMessage[]; caption?: string }) {
  const uid = useId().replace(/:/g, '')
  const [figRef, seen] = useSeen<HTMLElement>()
  const list = actors.map((a) => (typeof a === 'string' ? { name: a, sub: undefined } : a))
  const n = list.length
  const hasPhases = messages.some((m) => m.phase)
  const gutter = hasPhases ? 34 : 0
  const colW = (W - gutter) / n
  const cx = (i: number) => gutter + colW * (i + 0.5)
  const top = 74

  // Lay the rows out first so lifelines and phase bands know the total height.
  let y = top
  const rows = messages.map((m, i) => {
    const self = m.from === m.to
    const span = Math.abs(cx(m.to) - cx(m.from))
    const lines = wrap(m.text, Math.max(14, Math.floor((self ? colW * 1.4 : Math.max(span, colW) + colW * 0.4) / CHAR)))
    const labelH = lines.length * 16 + 8
    const h = self ? labelH + 22 : labelH + 26 + (m.code ? 36 : 0)
    const row = { m, i, self, lines, labelH, y, h, span }
    y += h
    return row
  })
  const H = y + 14

  const phases: { name: string; y0: number; y1: number }[] = []
  rows.forEach((r) => {
    if (r.m.phase) phases.push({ name: r.m.phase, y0: r.y, y1: H - 6 })
    else if (!phases.length && hasPhases) phases.push({ name: '', y0: r.y, y1: H - 6 })
  })
  phases.forEach((p, i) => i < phases.length - 1 && (p.y1 = phases[i + 1].y0))

  return (
    <figure ref={figRef} data-play={seen || undefined} className="not-prose my-8 rounded-2xl border border-border bg-card">
      <ScrollArea className="overflow-x-auto rounded-t-2xl">
      <svg viewBox={`0 0 ${W} ${H}`} className="block min-w-[40rem] w-full" role="img" aria-label={messages.map((m, i) => `${i + 1}. ${m.text}${m.code ? ` (${m.code})` : ''}`).join(' ')}>
        <defs>
          <marker id={`${uid}-req`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto">
            <path d="M1 1.5 9 5 1 8.5" fill="none" stroke="var(--primary)" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
          </marker>
          <marker id={`${uid}-rep`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto">
            <path d="M1 1.5 9 5 1 8.5" fill="none" stroke="var(--success)" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
          </marker>
        </defs>

        {phases.map((p, i) => (
          <g key={i}>
            <rect x="0" y={p.y0 - 4} width={W} height={Math.max(p.y1 - p.y0, 0)} fill={i % 2 ? 'transparent' : 'color-mix(in oklab, var(--primary) 4%, transparent)'} />
            {p.name && (
              <text
                transform={`translate(20 ${(p.y0 + p.y1) / 2}) rotate(-90)`}
                textAnchor="middle"
                className="fill-muted-foreground font-sans text-[11px] font-semibold uppercase"
                style={{ letterSpacing: '0.1em' }}
              >
                {p.name}
              </text>
            )}
            {i > 0 && <line x1="0" x2={W} y1={p.y0 - 4} y2={p.y0 - 4} stroke="var(--border)" />}
          </g>
        ))}

        {list.map((a, i) => {
          const Icon = iconFor(a.name)
          const w = Math.min(colW - 24, 190)
          return (
            <g key={a.name}>
              <line x1={cx(i)} x2={cx(i)} y1="54" y2={H - 8} stroke="var(--border-strong)" strokeDasharray="3 5" />
              <rect x={cx(i) - w / 2} y="8" width={w} height="44" rx="12" fill="var(--primary-soft)" stroke="color-mix(in oklab, var(--primary) 35%, transparent)" />
              <Icon x={cx(i) - w / 2 + 12} y={21} width={18} height={18} stroke="var(--primary)" strokeWidth={1.8} />
              <text x={cx(i) - w / 2 + 38} y={a.sub ? 27 : 34} className="fill-foreground font-sans text-[13px] font-semibold">
                {a.name}
              </text>
              {a.sub && (
                <text x={cx(i) - w / 2 + 38} y={42} className="fill-muted-foreground font-sans text-[11px]">
                  {a.sub}
                </text>
              )}
            </g>
          )
        })}

        {rows.map(({ m, i, self, lines, labelH, y: ry, span }) => {
          const x1 = cx(m.from)
          const x2 = cx(m.to)
          const mid = (x1 + x2) / 2
          const labelW = Math.max(...lines.map((l) => l.length)) * CHAR + 38
          const ay = ry + labelH + 12
          const tone = m.reply ? 'var(--success)' : 'var(--primary)'
          const badge = (bx: number, by: number) => (
            <g>
              <circle cx={bx} cy={by} r="9" fill={tone} />
              <text x={bx} y={by + 4} textAnchor="middle" className="font-sans text-[11px] font-bold" style={{ fill: m.reply ? 'var(--success-foreground)' : 'var(--primary-foreground)' }}>
                {i + 1}
              </text>
            </g>
          )

          if (self) {
            return (
              <g key={i}>
                <rect x={x1 - labelW / 2 - 2} y={ry + 1} width={labelW + 4} height={labelH + 4} rx="11" fill="var(--primary-soft)" stroke="color-mix(in oklab, var(--primary) 40%, transparent)" />
                {badge(x1 - labelW / 2 + 15, ry + 3 + (labelH + 2) / 2)}
                {lines.map((l, k) => (
                  <text key={k} x={x1 - labelW / 2 + 30} y={ry + 21 + k * 16} className="fill-foreground font-sans text-[13px]">
                    {l}
                  </text>
                ))}
              </g>
            )
          }

          const dir = Math.sign(x2 - x1)
          const code = m.code
          const cw = code ? Math.min(code.length * MONO + 22, span + colW * 0.6) : 0
          return (
            <g key={i}>
              <rect x={mid - labelW / 2} y={ry + 2} width={labelW} height={labelH - 2} rx="9" fill="var(--card)" stroke="var(--border)" />
              {badge(mid - labelW / 2 + 15, ry + 2 + (labelH - 2) / 2)}
              {lines.map((l, k) => (
                <text key={k} x={mid - labelW / 2 + 30} y={ry + 21 + k * 16} className="fill-foreground font-sans text-[13px]">
                  {l}
                </text>
              ))}
              <line
                x1={x1 + dir * 5}
                x2={x2 - dir * 5}
                y1={ay}
                y2={ay}
                stroke={tone}
                strokeWidth="1.6"
                strokeDasharray={m.reply ? '5 4' : undefined}
                markerEnd={`url(#${uid}-${m.reply ? 'rep' : 'req'})`}
              />
              <circle
                cx={x1 + dir * 5}
                cy={ay}
                r="3.5"
                fill={tone}
                className="seq-dot"
                style={{ '--dx': `${x2 - x1 - dir * 10}px`, '--delay': `${i * 0.7}s` } as CSSProperties}
              />
              {code && (
                <g>
                  <rect x={mid - cw / 2} y={ay + 9} width={cw} height="24" rx="6" fill="var(--code-bg)" stroke="var(--border)" />
                  <text x={mid} y={ay + 25} textAnchor="middle" className="fill-muted-foreground font-mono text-[11.5px]">
                    {code}
                  </text>
                </g>
              )}
            </g>
          )
        })}
      </svg>
      </ScrollArea>
      <p className={hint}>Swipe sideways to see the whole diagram.</p>
      {caption && <figcaption className="border-t border-border px-4 py-2.5 text-[13px] text-muted-foreground">{caption}</figcaption>}
    </figure>
  )
}

export type FlowNode = {
  id: string
  label: string
  sub?: string
  x: number
  y: number
  tone?: 'primary' | 'success' | 'warning' | 'danger' | 'muted'
  /** `decision` draws a diamond. */
  shape?: 'box' | 'decision'
}
export type FlowEdge = { from: string; to: string; label?: string; dashed?: boolean; bend?: number }

const toneVar = { primary: 'var(--primary)', success: 'var(--success)', warning: 'var(--warning)', danger: 'var(--danger)', muted: 'var(--muted-foreground)' }

/** Where a ray from the node's center toward (tx, ty) leaves its box or diamond. */
function edgePoint(n: FlowNode, tx: number, ty: number, pad = 4) {
  const hw = n.shape === 'decision' ? 96 : 94
  const hh = n.shape === 'decision' ? 42 : 30
  const dx = tx - n.x
  const dy = ty - n.y
  const t = n.shape === 'decision' ? 1 / (Math.abs(dx) / hw + Math.abs(dy) / hh || 1) : Math.min(hw / (Math.abs(dx) || 1e-6), hh / (Math.abs(dy) || 1e-6))
  const len = Math.hypot(dx, dy) || 1
  return { x: n.x + dx * t + (dx / len) * pad, y: n.y + dy * t + (dy / len) * pad }
}

/** A node-and-arrow flow chart drawn as SVG, for state machines and decision flows. Coordinates are in a 760-wide space. */
export function Flow({ nodes, edges, height = 300, caption }: { nodes: FlowNode[]; edges: FlowEdge[]; height?: number; caption?: string }) {
  const uid = useId().replace(/:/g, '')
  const [figRef, seen] = useSeen<HTMLElement>()
  const by = Object.fromEntries(nodes.map((n) => [n.id, n]))
  return (
    <figure ref={figRef} data-play={seen || undefined} className="not-prose my-8 rounded-2xl border border-border bg-card">
      <ScrollArea className="overflow-x-auto rounded-t-2xl">
      <svg viewBox={`0 0 ${W} ${height}`} className="block min-w-[40rem] w-full" role="img" aria-label={edges.map((e) => `${by[e.from].label} to ${by[e.to].label}${e.label ? `: ${e.label}` : ''}`).join('. ')}>
        <defs>
          <pattern id={`${uid}-grid`} width="22" height="22" patternUnits="userSpaceOnUse">
            <circle cx="1" cy="1" r="1" fill="var(--border)" />
          </pattern>
          <marker id={`${uid}-arrow`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto">
            <path d="M1 1.5 9 5 1 8.5" fill="none" stroke="var(--muted-foreground)" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
          </marker>
        </defs>
        <rect width={W} height={height} fill={`url(#${uid}-grid)`} opacity="0.7" />

        {edges.map((e, i) => {
          const a = by[e.from]
          const b = by[e.to]
          const bend = e.bend ?? 0
          const mx = (a.x + b.x) / 2
          const my = (a.y + b.y) / 2
          const nx = -(b.y - a.y)
          const ny = b.x - a.x
          const nl = Math.hypot(nx, ny) || 1
          const qx = mx + (nx / nl) * bend
          const qy = my + (ny / nl) * bend
          const p1 = edgePoint(a, bend ? qx : b.x, bend ? qy : b.y)
          const p2 = edgePoint(b, bend ? qx : a.x, bend ? qy : a.y, 6)
          // For a quadratic Bézier the midpoint is (p1 + 2q + p2) / 4.
          const lx = bend ? (p1.x + 2 * qx + p2.x) / 4 : (p1.x + p2.x) / 2
          const ly = bend ? (p1.y + 2 * qy + p2.y) / 4 : (p1.y + p2.y) / 2
          const lw = e.label ? e.label.length * 6.9 + 20 : 0
          return (
            <g key={i}>
              <path
                d={bend ? `M${p1.x} ${p1.y} Q${qx} ${qy} ${p2.x} ${p2.y}` : `M${p1.x} ${p1.y} L${p2.x} ${p2.y}`}
                fill="none"
                stroke="var(--muted-foreground)"
                strokeWidth="1.5"
                markerEnd={`url(#${uid}-arrow)`}
                className={e.dashed ? 'edge-flow' : undefined}
                strokeDasharray={e.dashed ? '5 5' : undefined}
              />
              {e.label && (
                <g>
                  <rect x={lx - lw / 2} y={ly - 11} width={lw} height="22" rx="11" fill="var(--card)" stroke="var(--border-strong)" />
                  <text x={lx} y={ly + 4} textAnchor="middle" className="fill-foreground font-sans text-[12px]">
                    {e.label}
                  </text>
                </g>
              )}
            </g>
          )
        })}

        {nodes.map((n) => {
          const tone = toneVar[n.tone ?? 'primary']
          const fill = `color-mix(in oklab, ${tone} 11%, var(--card))`
          const stroke = `color-mix(in oklab, ${tone} 55%, var(--card))`
          return (
            <g key={n.id}>
              {n.shape === 'decision' ? (
                <polygon points={`${n.x},${n.y - 42} ${n.x + 96},${n.y} ${n.x},${n.y + 42} ${n.x - 96},${n.y}`} fill={fill} stroke={stroke} strokeWidth="1.5" />
              ) : (
                <rect x={n.x - 94} y={n.y - 30} width="188" height="60" rx="14" fill={fill} stroke={stroke} strokeWidth="1.5" />
              )}
              <text x={n.x} y={n.sub ? n.y - 3 : n.y + 5} textAnchor="middle" className="fill-foreground font-sans text-[14px] font-semibold">
                {n.label}
              </text>
              {n.sub && (
                <text x={n.x} y={n.y + 15} textAnchor="middle" className="fill-muted-foreground font-sans text-[11.5px]">
                  {n.sub}
                </text>
              )}
            </g>
          )
        })}
      </svg>
      </ScrollArea>
      <p className={hint}>Swipe sideways to see the whole diagram.</p>
      {caption && <figcaption className="border-t border-border px-4 py-2.5 text-[13px] text-muted-foreground">{caption}</figcaption>}
    </figure>
  )
}

export type BarDatum = { label: string; value: number; note?: string }

const fmt = new Intl.NumberFormat('en-US')

/** Horizontal bar chart. Bars are scaled to the largest value; the number is always printed, so color is not the only cue. */
export function Bars({
  title,
  unit,
  data,
  lowerIsBetter,
}: {
  title: string
  unit?: string
  data: BarDatum[]
  lowerIsBetter?: boolean
}): ReactNode {
  const max = Math.max(...data.map((d) => d.value))
  const best = lowerIsBetter ? Math.min(...data.map((d) => d.value)) : max

  return (
    <figure className="not-prose my-7 rounded-xl border border-border bg-card p-4 sm:p-5">
      <figcaption className="mb-3 flex flex-wrap items-baseline justify-between gap-x-4 text-sm font-semibold text-foreground">
        {title}
        <span className="text-xs font-normal text-muted-foreground">
          {unit}
          {unit && ' · '}
          {lowerIsBetter ? 'lower is better' : 'higher is better'}
        </span>
      </figcaption>
      <ul className="space-y-2.5">
        {data.map((d) => {
          const brand = brandOf(d.label)
          return (
            <li key={d.label} className="grid grid-cols-1 items-center gap-x-3 gap-y-1 text-xs sm:grid-cols-[14rem_1fr]">
              <span className="flex items-center gap-2 text-foreground" title={d.label}>
                {brand && <BrandMark brand={brand} className="size-5" />}
                <span className="truncate">{d.label}</span>
              </span>
              <div className="flex items-center gap-2">
                <div className="h-5 flex-1 overflow-hidden rounded bg-muted">
                  <div
                    className={`h-full rounded ${brand ? '' : d.value === best ? 'bg-primary' : 'bg-primary/45'}`}
                    style={{ width: `${Math.max((d.value / max) * 100, 1)}%`, ...(brand && { background: brand.name === 'Inertia.Net' && /JIT/.test(d.label) ? `color-mix(in oklab, ${brand.bar} 58%, var(--card))` : brand.bar }) }}
                  />
                </div>
                <span className="w-16 text-right font-mono tabular-nums text-foreground">{fmt.format(d.value)}</span>
              </div>
            </li>
          )
        })}
      </ul>
    </figure>
  )
}

/** A row of the frameworks compared, each with its logo. */
export function Contenders({ names }: { names: string[] }) {
  return (
    <ul className="not-prose my-7 flex flex-wrap gap-2.5">
      {names.map((n) => {
        const b = brandOf(n)
        return (
          <li key={n} className="flex items-center gap-2.5 rounded-full border border-border bg-card py-1.5 pl-2 pr-4 text-sm font-medium shadow-[var(--shadow-lift)]">
            {b && <BrandMark brand={b} className="size-7" />}
            {n}
          </li>
        )
      })}
    </ul>
  )
}
