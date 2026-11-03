---
version: 1
slug: "docs-inertia-net-docs"
primary_target: "docs/Inertia.Net.Docs"
related_targets: []
---

# Docs site surface brief

Scope: every documentation page and its shell (header, sidebar, contents, search, MDX components). Mode: Read. Home inherits tokens and shared chrome only.

Audience and job: .NET developers beside an editor and terminal, copying C# and client snippets and switching React/Vue/Svelte; AI agents read llms.txt. Success: find the snippet or answer fast and keep reading forward.

Constraints: SSR under Bun, light/dark/system themes, prefers-reduced-motion, no new runtime dependencies, MDX content factually unchanged.

## Direction contract

THESIS: The category standard played straight at Stripe/Vercel docs craft: a quiet, exact reading column inside a crisp three-column shell. Refuses the stock shadcn template look (flat gray, default borders, no motion, no wayfinding beyond the sidebar).

OWN-WORLD: Neutrals faintly tinted toward .NET violet; one violet accent for location, links and focus only. Inter for UI and prose, JetBrains Mono for code. Hairline rules, 12px code/card radii, soft offset shadows only on floating layers. Translucent sticky header with backdrop blur over scrolling content.

STORY: The reader always knows where they are (sidebar pill, breadcrumb, sliding contents marker, reading progress), copies code with clear feedback, keeps one framework choice across every tab group, and moves on through prominent previous/next.

FIRST VIEWPORT: 56px glass header (brand, Docs/AI agents/Samples, wide search with shortcut, theme, GitHub); left sidebar 240px; centre column about 70ch with breadcrumb, 40px title, lead; right "On this page" rail with a sliding marker. Below xl, contents collapse into a disclosure under the title.

FORM: canon (category standard), chosen by the user from the direction round. seed:f663071772966e10

Signature interaction: the contents marker that slides to the active section, paired with a heading flash on anchor jumps; page changes crossfade the article via view transitions. Motion is exponential ease-out from visible defaults and drops to none under reduced motion.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
