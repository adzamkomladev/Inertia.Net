import { Check, Copy } from 'lucide-react'
import { useEffect, useRef, useState, type ComponentProps } from 'react'
import { cn } from '../lib/utils'

const labels: Record<string, string> = {
  cs: 'C#',
  csharp: 'C#',
  ts: 'TypeScript',
  tsx: 'TSX',
  js: 'JavaScript',
  jsx: 'JSX',
  sh: 'Shell',
  bash: 'Bash',
  shell: 'Shell',
  json: 'JSON',
  jsonc: 'JSON',
  html: 'HTML',
  xml: 'XML',
  css: 'CSS',
  diff: 'Diff',
  yaml: 'YAML',
  yml: 'YAML',
  ini: 'INI',
  text: 'Text',
  http: 'HTTP',
  vue: 'Vue',
  svelte: 'Svelte',
}

export function CodeBlock({ className, children, ...props }: ComponentProps<'pre'>) {
  const ref = useRef<HTMLPreElement>(null)
  const [copied, setCopied] = useState(false)
  const [language, setLanguage] = useState(() => className?.match(/language-([\w-]+)/)?.[1] ?? 'text')

  useEffect(() => {
    const codeLanguage = ref.current?.querySelector('code')?.className.match(/language-([\w-]+)/)?.[1]
    if (codeLanguage) setLanguage(codeLanguage)
  }, [children])

  const copy = async () => {
    const text = ref.current?.querySelector('code')?.textContent ?? ''
    try {
      await navigator.clipboard.writeText(text.replace(/\n$/, ''))
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1600)
    } catch {
      // Clipboard access can be denied; the code remains selectable.
    }
  }

  return (
    <figure className="group/code my-6 overflow-hidden rounded-xl border border-border bg-[#fbfbfc] dark:bg-[#0d1117]">
      <figcaption className="flex h-9 items-center justify-between border-b border-border/80 bg-muted/30 px-3.5">
        <span className="font-mono text-[11px] font-medium text-muted-foreground">{labels[language] ?? language}</span>
        <button
          type="button"
          onClick={copy}
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-[11px] text-muted-foreground opacity-100 transition-opacity hover:bg-accent hover:text-foreground focus-visible:opacity-100 sm:opacity-0 sm:group-hover/code:opacity-100"
          aria-label={copied ? 'Copied' : 'Copy code'}
        >
          {copied ? <Check className="size-3.5 text-success" /> : <Copy className="size-3.5" />}
          {copied ? 'Copied' : 'Copy'}
        </button>
      </figcaption>
      <div className="scrollbar-thin overflow-x-auto">
        <pre ref={ref} className={cn(className, 'm-0 bg-transparent py-3.5')} {...props}>
          {children}
        </pre>
      </div>
    </figure>
  )
}
