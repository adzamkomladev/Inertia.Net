import { Braces, Cpu, Database, Globe, Radio, RotateCcw, Server } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import type { ComponentType, CSSProperties, ReactNode, SVGProps } from 'react'
import { BrandMark, brandOf } from './Brands'

const MAX_W = 760
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

/** True once the element has been on screen; diagrams use it to play their motion when first seen. */
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

/** The element's rendered width, so a diagram can lay itself out 1:1 instead of scaling its text down. */
function useWidth<T extends HTMLElement>(initial: number) {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(initial)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setWidth(Math.round(el.getBoundingClientRect().width) || initial)
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [initial])
  return [ref, width] as const
}

function Footer({ caption, onReplay }: { caption?: string; onReplay: () => void }) {
  return (
    <div className="flex items-center gap-3 border-t border-border px-4 py-2">
      {caption ? <figcaption className="min-w-0 flex-1 text-[13px] leading-5 text-muted-foreground">{caption}</figcaption> : <span className="flex-1" />}
      <ReplayButton onClick={onReplay} />
    </div>
  )
}

function ReplayButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="group inline-flex min-h-8 shrink-0 items-center gap-1.5 rounded-full px-2.5 text-xs font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
    >
      <RotateCcw className="size-3.5 transition-transform duration-500 group-hover:-rotate-180" />
      Replay
    </button>
  )
}

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

/** Hard-wraps a monospace payload that cannot fit on one line. */
function chunk(text: string, max: number): string[] {
  if (text.length <= max) return [text]
  // Break at spaces first, and only split a single long word when it cannot fit by itself.
  return wrap(text, max).flatMap((l) => {
    const out: string[] = []
    for (let i = 0; i < l.length; i += max) out.push(l.slice(i, i + max))
    return out
  })
}

const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), Math.max(lo, hi))

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
 * that travels along each arrow. `to === from` draws a note on that actor. The layout follows the width it is given,
 * so labels wrap on a phone instead of the diagram scrolling.
 */
export function Sequence({ actors, messages, caption }: { actors: Actor[]; messages: SequenceMessage[]; caption?: string }) {
  const uid = useId().replace(/:/g, '')
  const [figRef, seen] = useSeen<HTMLElement>()
  const [wrapRef, measured] = useWidth<HTMLDivElement>(MAX_W)
  const [run, setRun] = useState(0)
  const W = clamp(measured, 300, MAX_W)
  const narrow = W < 560
  const list = actors.map((a) => (typeof a === 'string' ? { name: a, sub: undefined } : a))
  const n = list.length
  const hasPhases = messages.some((m) => m.phase)
  const gutter = hasPhases ? (narrow ? 28 : 34) : 0
  const colW = (W - gutter) / n
  const cx = (i: number) => gutter + colW * (i + 0.5)
  const top = 74

  // Lay the rows out first so lifelines and phase bands know the total height.
  let y = top
  const rows = messages.map((m, i) => {
    const self = m.from === m.to
    const span = Math.abs(cx(m.to) - cx(m.from))
    const room = Math.min(self ? colW * 1.4 : Math.max(span, colW) + colW * 0.4, W - 24)
    const lines = wrap(m.text, Math.max(10, Math.floor((room - 38) / CHAR)))
    const labelH = lines.length * 16 + 8
    const codeRoom = Math.min(Math.max(span, colW) + colW * 0.6, W - 16)
    const codeLines = m.code ? chunk(m.code, Math.max(8, Math.floor((codeRoom - 20) / MONO))) : []
    const codeH = codeLines.length ? 14 + codeLines.length * 15 : 0
    const h = self ? labelH + 22 : labelH + 26 + (codeH ? codeH + 10 : 0)
    const row = { m, i, self, lines, labelH, codeLines, codeH, y, h, span }
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
    <figure ref={figRef} data-play={seen || undefined} className="not-prose my-8 overflow-hidden rounded-2xl border border-border bg-card">
      <div ref={wrapRef}>
        <svg viewBox={`0 0 ${W} ${H}`} width={W} height={H} className="block max-w-full" role="img" aria-label={messages.map((m, i) => `${i + 1}. ${m.text}${m.code ? ` (${m.code})` : ''}`).join(' ')}>
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
                  transform={`translate(${narrow ? 16 : 20} ${(p.y0 + p.y1) / 2}) rotate(-90)`}
                  textAnchor="middle"
                  className="fill-muted-foreground font-sans text-[11px] font-semibold uppercase"
                  style={{ letterSpacing: narrow ? '0.06em' : '0.1em' }}
                >
                  {p.name}
                </text>
              )}
              {i > 0 && <line x1="0" x2={W} y1={p.y0 - 4} y2={p.y0 - 4} stroke="var(--border)" />}
            </g>
          ))}

          {list.map((a, i) => {
            const Icon = iconFor(a.name)
            const w = narrow ? colW - 8 : Math.min(colW - 24, 190)
            const showSub = !!a.sub && colW >= 150
            return (
              <g key={a.name}>
                <line x1={cx(i)} x2={cx(i)} y1="54" y2={H - 8} stroke="var(--border-strong)" strokeDasharray="3 5" />
                <rect x={cx(i) - w / 2} y="8" width={w} height="44" rx="12" fill="var(--primary-soft)" stroke="color-mix(in oklab, var(--primary) 35%, transparent)" />
                {narrow ? (
                  <text x={cx(i)} y={26} textAnchor="middle" className="fill-foreground font-sans text-[12px] font-semibold">
                    {wrap(a.name, Math.max(6, Math.floor((w - 8) / 7)))
                      .slice(0, 2)
                      .map((l, k) => (
                        <tspan key={k} x={cx(i)} dy={k === 0 ? 0 : 14}>
                          {l}
                        </tspan>
                      ))}
                  </text>
                ) : (
                  <>
                    <Icon x={cx(i) - w / 2 + 12} y={21} width={18} height={18} stroke="var(--primary)" strokeWidth={1.8} />
                    <text x={cx(i) - w / 2 + 38} y={showSub ? 27 : 34} className="fill-foreground font-sans text-[13px] font-semibold">
                      {a.name}
                    </text>
                    {showSub && (
                      <text x={cx(i) - w / 2 + 38} y={42} className="fill-muted-foreground font-sans text-[11px]">
                        {a.sub}
                      </text>
                    )}
                  </>
                )}
              </g>
            )
          })}

          {rows.map(({ m, i, self, lines, labelH, codeLines, codeH, y: ry, span }) => {
            const x1 = cx(m.from)
            const x2 = cx(m.to)
            const labelW = Math.min(Math.max(...lines.map((l) => l.length)) * CHAR + 38, W - 16)
            const mid = clamp((x1 + x2) / 2, labelW / 2 + 8, W - labelW / 2 - 8)
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
              const sx = clamp(x1, labelW / 2 + 10, W - labelW / 2 - 10)
              return (
                <g key={i}>
                  <rect x={sx - labelW / 2 - 2} y={ry + 1} width={labelW + 4} height={labelH + 4} rx="11" fill="var(--primary-soft)" stroke="color-mix(in oklab, var(--primary) 40%, transparent)" />
                  {badge(sx - labelW / 2 + 15, ry + 3 + (labelH + 2) / 2)}
                  {lines.map((l, k) => (
                    <text key={k} x={sx - labelW / 2 + 30} y={ry + 21 + k * 16} className="fill-foreground font-sans text-[13px]">
                      {l}
                    </text>
                  ))}
                </g>
              )
            }

            const dir = Math.sign(x2 - x1)
            const cw = codeLines.length ? Math.min(Math.max(...codeLines.map((l) => l.length)) * MONO + 22, W - 16) : 0
            const cxm = clamp((x1 + x2) / 2, cw / 2 + 8, W - cw / 2 - 8)
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
                  key={`${run}-${i}`}
                  cx={x1 + dir * 5}
                  cy={ay}
                  r="3.5"
                  fill={tone}
                  className="seq-dot"
                  style={{ '--dx': `${x2 - x1 - dir * 10}px`, '--delay': `${i * 1.1}s` } as CSSProperties}
                />
                {codeLines.length > 0 && (
                  <g>
                    <rect x={cxm - cw / 2} y={ay + 9} width={cw} height={codeH} rx="6" fill="var(--code-bg)" stroke="var(--border)" />
                    {codeLines.map((l, k) => (
                      <text key={k} x={cxm} y={ay + 25 + k * 15} textAnchor="middle" className="fill-muted-foreground font-mono text-[11.5px]">
                        {l}
                      </text>
                    ))}
                  </g>
                )}
              </g>
            )
          })}
        </svg>
      </div>
      <Footer caption={caption} onReplay={() => setRun((r) => r + 1)} />
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
  /** Box width, or diamond width. Defaults to 188 and 192. */
  w?: number
}
export type FlowEdge = { from: string; to: string; label?: string; dashed?: boolean; bend?: number }
type FlowSpec = { nodes: FlowNode[]; edges: FlowEdge[]; height: number }

const toneVar = { primary: 'var(--primary)', success: 'var(--success)', warning: 'var(--warning)', danger: 'var(--danger)', muted: 'var(--muted-foreground)' }

const halfW = (n: FlowNode) => (n.w ?? (n.shape === 'decision' ? 192 : 188)) / 2

/** Where a ray from the node's center toward (tx, ty) leaves its box or diamond. */
function edgePoint(n: FlowNode, tx: number, ty: number, pad = 4) {
  const hw = halfW(n)
  const hh = n.shape === 'decision' ? 42 : 30
  const dx = tx - n.x
  const dy = ty - n.y
  const t = n.shape === 'decision' ? 1 / (Math.abs(dx) / hw + Math.abs(dy) / hh || 1) : Math.min(hw / (Math.abs(dx) || 1e-6), hh / (Math.abs(dy) || 1e-6))
  const len = Math.hypot(dx, dy) || 1
  return { x: n.x + dx * t + (dx / len) * pad, y: n.y + dy * t + (dy / len) * pad }
}

/**
 * A node-and-arrow flow chart drawn as SVG, for state machines and decision flows. Coordinates are in a 760-wide space.
 * Give `narrow` a second layout in a 360-wide space and it replaces the wide one on small screens, so nothing scrolls.
 */
export function Flow({ nodes, edges, height = 300, caption, narrow }: { nodes: FlowNode[]; edges: FlowEdge[]; height?: number; caption?: string; narrow?: FlowSpec }) {
  const uid = useId().replace(/:/g, '')
  const [figRef, seen] = useSeen<HTMLElement>()
  const [wrapRef, width] = useWidth<HTMLDivElement>(MAX_W)
  const [run, setRun] = useState(0)
  const compact = !!narrow && width < 600
  const spec: FlowSpec = compact ? narrow : { nodes, edges, height }
  const Wc = compact ? 360 : MAX_W
  const by = Object.fromEntries(spec.nodes.map((n) => [n.id, n]))
  return (
    <figure ref={figRef} data-play={seen || undefined} className="not-prose my-8 overflow-hidden rounded-2xl border border-border bg-card">
      <div ref={wrapRef}>
        <svg viewBox={`0 0 ${Wc} ${spec.height}`} className={compact ? 'mx-auto block h-auto w-full max-w-[440px]' : 'block h-auto w-full'} role="img" aria-label={spec.edges.map((e) => `${by[e.from].label} to ${by[e.to].label}${e.label ? `: ${e.label}` : ''}`).join('. ')}>
          <defs>
            <pattern id={`${uid}-grid`} width="22" height="22" patternUnits="userSpaceOnUse">
              <circle cx="1" cy="1" r="1" fill="var(--border)" />
            </pattern>
            <marker id={`${uid}-arrow`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto">
              <path d="M1 1.5 9 5 1 8.5" fill="none" stroke="var(--muted-foreground)" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
            </marker>
          </defs>
          <rect width={Wc} height={spec.height} fill={`url(#${uid}-grid)`} opacity="0.7" />

          {spec.edges.map((e, i) => {
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
              <g key={`${run}-${i}`}>
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

          {spec.nodes.map((n, i) => {
            const t = toneVar[n.tone ?? 'primary']
            const fill = `color-mix(in oklab, ${t} 11%, var(--card))`
            const stroke = `color-mix(in oklab, ${t} 55%, var(--card))`
            const hw = halfW(n)
            return (
              <g key={`${run}-${n.id}`} className="flow-node" style={{ animationDelay: `${i * 70}ms` }}>
                {n.shape === 'decision' ? (
                  <polygon points={`${n.x},${n.y - 42} ${n.x + hw},${n.y} ${n.x},${n.y + 42} ${n.x - hw},${n.y}`} fill={fill} stroke={stroke} strokeWidth="1.5" />
                ) : (
                  <rect x={n.x - hw} y={n.y - 30} width={hw * 2} height="60" rx="14" fill={fill} stroke={stroke} strokeWidth="1.5" />
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
      </div>
      <Footer caption={caption} onReplay={() => setRun((r) => r + 1)} />
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
  const [figRef, seen] = useSeen<HTMLElement>()
  const [armed, setArmed] = useState(false)
  const [run, setRun] = useState(0)
  useEffect(() => setArmed(true), [])

  return (
    <figure ref={figRef} data-armed={armed || undefined} data-play={seen || undefined} className="not-prose my-7 rounded-xl border border-border bg-card p-4 sm:p-5">
      <figcaption className="mb-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-sm font-semibold text-foreground">
        {title}
        <span className="flex items-center gap-2 text-xs font-normal text-muted-foreground">
          <span>
            {unit}
            {unit && ' · '}
            {lowerIsBetter ? 'lower is better' : 'higher is better'}
          </span>
          <ReplayButton onClick={() => setRun((r) => r + 1)} />
        </span>
      </figcaption>
      <ul className="space-y-2.5">
        {data.map((d, i) => {
          const brand = brandOf(d.label)
          return (
            <li key={`${run}-${d.label}`} className="grid grid-cols-1 items-center gap-x-3 gap-y-1 text-xs sm:grid-cols-[14rem_1fr]">
              <span className="flex items-center gap-2 text-foreground" title={d.label}>
                {brand && <BrandMark brand={brand} className="size-5" />}
                <span className="truncate">{d.label}</span>
              </span>
              <div className="flex items-center gap-2">
                <div className="h-5 flex-1 overflow-hidden rounded bg-muted">
                  <div
                    className={`bar-grow h-full rounded ${brand ? '' : d.value === best ? 'bg-primary' : 'bg-primary/45'}`}
                    style={{ animationDelay: `${i * 140}ms`, width: `${Math.max((d.value / max) * 100, 1)}%`, ...(brand && { background: brand.name === 'Inertia.Net' && /JIT/.test(d.label) ? `color-mix(in oklab, ${brand.bar} 58%, var(--card))` : brand.bar }) }}
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
