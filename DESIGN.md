---
name: Inertia.Net Docs
description: A quiet, exact reading column inside a crisp three-column documentation shell, tinted toward .NET violet.
colors:
  primary: "oklch(0.47 0.24 285)"
  primary-foreground: "oklch(0.99 0 0)"
  primary-soft: "oklch(0.955 0.028 286)"
  ring: "oklch(0.55 0.2 285)"
  background: "oklch(0.993 0.002 286)"
  foreground: "oklch(0.21 0.012 286)"
  card: "oklch(1 0 0)"
  popover: "oklch(1 0 0)"
  muted: "oklch(0.967 0.004 286)"
  muted-foreground: "oklch(0.47 0.014 286)"
  accent: "oklch(0.955 0.008 286)"
  secondary: "oklch(0.962 0.005 286)"
  border: "oklch(0.912 0.006 286)"
  border-strong: "oklch(0.84 0.01 286)"
  code-bg: "oklch(0.982 0.003 286)"
  header-bg: "oklch(0.993 0.002 286 / 0.78)"
  success: "oklch(0.55 0.16 155)"
  warning: "oklch(0.6 0.15 70)"
  danger: "oklch(0.56 0.21 27)"
  info: "oklch(0.55 0.15 245)"
typography:
  display:
    fontFamily: "Inter Variable, ui-sans-serif, system-ui, sans-serif"
    fontSize: "2.5rem"
    fontWeight: 600
    lineHeight: 1.15
    letterSpacing: "-0.03em"
    fontFeature: "'cv02', 'cv03', 'cv04', 'cv11'"
  headline:
    fontFamily: "Inter Variable, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "-0.02em"
  title:
    fontFamily: "Inter Variable, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1.1875rem"
    fontWeight: 600
    lineHeight: 1.4
    letterSpacing: "-0.02em"
  lead:
    fontFamily: "Inter Variable, ui-sans-serif, system-ui, sans-serif"
    fontSize: "17px"
    fontWeight: 400
    lineHeight: "28px"
  body:
    fontFamily: "Inter Variable, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.75
    fontFeature: "'cv02', 'cv03', 'cv04', 'cv11'"
  label:
    fontFamily: "Inter Variable, ui-sans-serif, system-ui, sans-serif"
    fontSize: "13px"
    fontWeight: 500
    lineHeight: "20px"
  label-section:
    fontFamily: "Inter Variable, ui-sans-serif, system-ui, sans-serif"
    fontSize: "12px"
    fontWeight: 600
    lineHeight: "16px"
  code:
    fontFamily: "JetBrains Mono Variable, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.75
    fontFeature: "'liga' 0"
  code-label:
    fontFamily: "JetBrains Mono Variable, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "11px"
    fontWeight: 500
    letterSpacing: "0.025em"
rounded:
  sm: "6px"
  md: "8px"
  lg: "10px"
  xl: "12px"
  dialog: "16px"
  full: "9999px"
spacing:
  hairline: "1px"
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  block: "28px"
  section: "48px"
  header: "56px"
  sidebar: "240px"
  contents-rail: "224px"
  article: "45rem"
  measure: "38rem"
  shell-max: "1440px"
components:
  sidebar-link:
    textColor: "{colors.muted-foreground}"
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    padding: "5px 10px"
  sidebar-link-hover:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.foreground}"
  sidebar-link-active:
    backgroundColor: "{colors.primary-soft}"
    textColor: "{colors.primary}"
  icon-button:
    textColor: "{colors.muted-foreground}"
    rounded: "{rounded.lg}"
    size: "36px"
  icon-button-hover:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.foreground}"
  search-trigger:
    backgroundColor: "{colors.card}"
    textColor: "{colors.muted-foreground}"
    rounded: "{rounded.lg}"
    height: "36px"
    width: "256px"
  code-block:
    backgroundColor: "{colors.code-bg}"
    textColor: "{colors.foreground}"
    typography: "{typography.code}"
    rounded: "{rounded.xl}"
    padding: "16px 18px"
  code-block-caption:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.muted-foreground}"
    typography: "{typography.code-label}"
    height: "40px"
  inline-code:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.sm}"
    padding: "0.12rem 0.36rem"
  card:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.xl}"
    padding: "16px"
  pager-card:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.xl}"
    padding: "16px 20px"
  tab:
    textColor: "{colors.muted-foreground}"
    rounded: "{rounded.md}"
    padding: "10px 10px"
  tab-selected:
    textColor: "{colors.foreground}"
  kbd:
    backgroundColor: "{colors.background}"
    textColor: "{colors.muted-foreground}"
    rounded: "{rounded.md}"
    height: "20px"
  search-dialog:
    backgroundColor: "{colors.popover}"
    rounded: "{rounded.dialog}"
    width: "672px"
---

# Design System: Inertia.Net Docs

## Overview

**Creative North Star: "The Exact Reference Desk"**

The category standard played straight: a quiet reading column set inside a crisp three-column shell (sidebar, article, contents rail), built for a developer with an editor and terminal open beside it. Code is the primary content, so code frames get the most deliberate chrome on the page and prose stays calm around them. Density is moderate: generous line height in prose, tight 13 to 14px wayfinding text in the rails.

Every neutral carries a faint violet tint (hue 286) so the single .NET violet accent reads as part of the page rather than pasted on. The accent marks location, links and focus, and nothing else. Depth is mostly tonal and hairline; real shadows are reserved for layers that float above the page. Motion is short, exponential ease-out, and exists to keep the reader oriented: the contents marker slides, the jumped-to heading flashes, the article crossfades on page change. All of it drops to none under reduced motion.

The build rejects the stock template look: flat gray neutrals, default borders, no motion, no wayfinding beyond the sidebar.

**Key Characteristics:**
- Violet-tinted neutrals with one violet accent, in light and dark themes.
- Inter for UI and prose, JetBrains Mono for code, with ligatures off.
- Hairline (1px) rules everywhere; 12px radii on code frames, cards, tables and callouts.
- Translucent sticky 56px header with backdrop blur and a scroll-driven reading-progress hairline.
- Wayfinding on every page: sidebar pill, breadcrumb, sliding contents marker, previous/next cards.

## Colors

A near-monochrome violet-tinted neutral field with a single saturated .NET violet and four semantic status hues used only inside callouts and feedback. Values in the frontmatter are the light theme; the dark theme redefines every token on `.dark` (recorded in the sidecar).

### Primary
- **.NET Violet** (primary): the brand purple (#512BD4 in spirit). Active sidebar text, active contents entry, header nav underline, breadcrumb section, link text and underlines, tab indicator, reading-progress bar, heading flash, caret and selection tint. Lightens to a softer lavender in dark mode for contrast.
- **Violet Wash** (primary-soft): the background of the active sidebar pill and of hovered or selected icon tiles in cards and search results.
- **Focus Violet** (ring): the 2px focus-visible outline, offset 2px.

### Neutral
- **Violet Paper** (background): page ground; also the translucent header base (header-bg, 78% light, 72% dark).
- **Ink** (foreground): headings and strong text. Running prose renders at 88% foreground mixed into background, a deliberate softening.
- **Card White** (card, popover): tables, cards, accordions, pager cards, search dialog.
- **Mist** (muted, secondary): inline code ground, code captions and table heads at 50 to 60% opacity.
- **Slate Violet** (muted-foreground): secondary text, inactive nav, captions, lead paragraphs.
- **Hover Wash** (accent): hover ground for nav links and icon buttons.
- **Hairline** (border) and **Firm Hairline** (border-strong): every rule and frame; border-strong for hover borders, blockquote rules, step markers and the bottom edge of keycaps.
- **Code Ground** (code-bg): the slightly lifted background of code frames and tab groups.

### Status
- **Info Blue, Success Green, Warning Amber, Danger Red** (info, success, warning, danger): callout tone only (6% tinted ground, 28% tinted hairline, full-strength icon) and the green "Copied" confirmation.

### Named Rules
**The One Accent Rule.** Violet is spent on location, links and focus. It never fills a large surface; the only solid violet fills are the brand tile and the 2px progress and tab indicators.

**The Tinted Neutral Rule.** No pure gray. Every neutral sits at hue 286 with chroma between 0.002 and 0.014.

**The One Tone Per Job Rule.** A callout derives its ground, hairline and icon from a single status token so both themes stay in step.

## Typography

**Display Font:** Inter Variable (with ui-sans-serif, system-ui)
**Body Font:** Inter Variable, with character variants cv02, cv03, cv04, cv11
**Label/Mono Font:** JetBrains Mono Variable (with ui-monospace, SFMono-Regular, Menlo)

**Character:** One neutral, precise sans for everything a reader scans, and one engineered mono for everything they copy. Hierarchy comes from size and tight negative tracking, not from weight extremes; nothing heavier than 600.

### Hierarchy
- **Display** (600, 2rem mobile to 2.5rem from sm, 1.15, -0.03em): the page title only, balanced.
- **Headline** (600, 1.5rem, 1.3, -0.02em): h2, preceded by a hairline rule and 2rem of padding, except when first.
- **Title** (600, 1.1875rem, 1.4): h3. h4 drops to 1rem.
- **Lead** (400, 17px / 28px, muted): the page description under the title, max 60ch.
- **Body** (400, 1rem, 1.75): prose, with running text held to a 38rem measure while code, tables and callouts use the full 45rem column.
- **Label** (500, 13px / 20px): breadcrumb, contents entries, card descriptions; sidebar links at 14px.
- **Section Label** (600, 12px, foreground, sentence case): sidebar group titles, "On this page", table headers (muted).
- **Code** (400, 13px, 1.75, ligatures off): code blocks; inline code at 0.84em, weight 500.
- **Code Label** (500, 11px mono, slight tracking): the language caption on code frames.

### Named Rules
**The Sentence Case Rule.** Labels and group headings are sentence case at normal tracking. No uppercase tracked labels.

**The Literal Code Rule.** Code ligatures are always off; what a reader sees is what they paste.

## Layout

A centred three-column shell, max 1440px, with 16 / 24 / 32px side gutters at base / sm / lg and a 24px column gap that opens to 48px from lg.

- **Header:** sticky, 56px tall, full width inside the 1440px frame. Brand, primary nav (Docs, AI agents, Samples), a 224 to 256px search trigger with shortcut key, theme toggle, GitHub.
- **Sidebar:** 240px, from lg; sticky under the header at full viewport height, scrolling independently with top and bottom fade masks. Groups spaced 24px, links stacked 1px apart.
- **Article:** fluid centre column capped at 45rem; breadcrumb, title, lead, then prose. Running text held to 38rem.
- **Contents rail:** 224px, from xl; sticky, with edit and back-to-top links under a hairline. Below xl it becomes a bordered disclosure under the title.
- **Rhythm:** a 4px base. Embedded blocks (code, tabs, tables, callouts, cards, accordions) sit at 28px vertical margin; h2 sections open with 52px above plus 32px padding over the rule; previous/next starts 64px below content.
- **Mobile:** the sidebar moves into a left drawer (min(86vw, 340px)); search collapses to an icon button below md.
- Anchor targets clear the header with a 5.5rem scroll padding and 6rem heading scroll margin.

## Elevation & Depth

A hybrid: the page is flat and tonal at rest, separated by hairlines and subtle ground shifts (background, code-bg, card, muted). Soft, violet-tinted, offset shadows appear only on floating layers and on hover lift for interactive cards. The header gains a hairline and a faint downward shadow only once the page scrolls.

### Shadow Vocabulary
- **Float** (`0 1px 2px oklch(0.2 0.02 286 / 0.06), 0 12px 32px -8px oklch(0.2 0.03 286 / 0.18)`): search dialog and mobile drawer.
- **Lift** (`0 1px 2px oklch(0.2 0.02 286 / 0.05), 0 6px 16px -6px oklch(0.2 0.03 286 / 0.14)`): hover state of link cards and previous/next cards, paired with a 1px rise.
- **Rest Whisper** (`0 1px 2px oklch(0.2 0.02 286 / 0.04)`): code frames and the search trigger, barely there.
- **Header Scrolled** (`0 1px 0 0 var(--border), 0 8px 24px -16px oklch(0.2 0.03 286 / 0.25)`): sticky header after 4px of scroll.

### Named Rules
**The Float-Only Shadow Rule.** A visible shadow means the element sits above the page or is responding to the pointer. Static content containers never carry more than the rest whisper.

**The Glass Header Rule.** The header is translucent with a strong backdrop blur and saturation, so scrolling content stays faintly visible beneath it.

## Shapes

Gently rounded and consistent. Containers that frame content (code blocks, tab groups, tables, callouts, cards, accordions, the mobile contents disclosure, prose images and details) use 12px. Interactive controls use 8 to 10px: nav links, tabs and keycaps at 8px, icon buttons, the search trigger and icon tiles at 10px. Inline code is 6px. The search dialog alone goes to 16px. Step markers are full circles on a 1px hairline spine. Every border is 1px; the only thicker strokes are the 2px violet tab indicator and progress bar and the 2px focus outline.

## Components

### Buttons and icon buttons
Quiet and unboxed until touched.
- **Icon button:** 36px square, 10px radius, muted icon (18 to 20px), hover to the accent wash with foreground icon, press scales to 0.95.
- **Copy button:** 28px tall, 8px radius, 11px label with icon. On success the copy icon scales out and a check scales in, the label turns success green for 1.8s, announced politely.
- **Primary fill:** violet ground with near-white text exists on the home page only (40px, 8px radius); the docs surface has no filled buttons.

### Search trigger and dialog
- **Trigger:** 36px, 10px radius, hairline border, card at 60% opacity, search icon, "Search docs", and a keycap showing the platform shortcut after mount. Hover firms the border and solidifies the ground.
- **Dialog:** 672px max, 10vh from top, 16px radius, popover ground, float shadow, over a blurred violet-black scrim. 56px input row with a violet search icon; grouped results with icon tiles that turn violet-wash when selected; matches marked with a 15% violet highlight; a muted footer row of keyboard hints.

### Code block
The signature container.
- 12px radius, hairline border, code-bg ground, rest whisper shadow.
- A 40px caption bar on a 50% muted ground: mono language label left, copy button right.
- Lines padded 18px, line hover tinted 6% violet, horizontal overflow signalled by an edge fade mask.

### Tabs
- Same frame as a code block; a muted tab strip of 12px medium labels with a 2px violet indicator that slides to the selected tab.
- Framework choice persists across every tab group and page; the clicked tab stays under the pointer when other groups change height. Arrow keys move between tabs.

### Cards / Containers
- **Corner Style:** 12px.
- **Background:** card.
- **Shadow Strategy:** none at rest; Lift on hover with a 1px rise (see Elevation).
- **Border:** hairline, firming to border-strong (link cards) or 40% violet (previous/next) on hover.
- **Internal Padding:** 16px (cards), 16px by 20px (previous/next).
- An optional 32px icon tile turns violet-wash on hover; a trailing arrow slides 4px and turns violet.

### Callouts
- 12px radius, 16px by 14px padding, 14px / 24px text, a status icon and a semibold title. Ground and hairline derive from the status tone (see Colors).

### Tables
- 12px frame on card, 50 to 60% muted header row with 12px semibold muted labels, 1px row rules at 70%, row hover tint, tabular numerals, inline code at 0.8em.

### Navigation
- **Sidebar:** 12px semibold group titles; 14px links at 5px by 10px padding, 8px radius. Hover: accent wash and a 2px nudge right. Active: violet-wash pill with violet medium text. Active entry auto-scrolls into view.
- **Header nav:** 14px medium muted links; active turns foreground with a 1px violet underline sitting on the header's bottom edge.
- **Breadcrumb:** 13px muted, chevron separator, current section in medium violet.
- **Contents rail:** 13px entries on a 1px left rail; h3 indented further. A 1px violet marker slides (300ms, ease-out) to the active entry, which turns medium violet.
- **Previous / next:** two cards, direction label with arrow that slides outward on hover, title turning violet.

### Steps
Numbered 28px circular markers (hairline-strong border, background fill, semibold tabular numeral) on a 1px spine running to the next step, with 15px / 28px muted body text.

### Reading progress and heading flash
A 2px violet bar along the header's bottom edge, driven purely by scroll timeline and hidden where unsupported. Jumping to an anchor flashes the target heading violet for about 30% of 1.6s, then settles.

## Do's and Don'ts

### Do:
- **Do** keep every neutral on hue 286 at low chroma, and define both a light and a `.dark` value for any new token.
- **Do** reserve violet for location, links and focus; mark the current place in every navigation element with it.
- **Do** frame code, tables, tabs, callouts and cards with a 1px hairline and a 12px radius.
- **Do** use the Float shadow only for dialogs and drawers and the Lift shadow only on hover of interactive cards.
- **Do** animate with the exponential ease-out (`cubic-bezier(0.16, 1, 0.3, 1)`) at 150 to 320ms on enter, faster ease-in on exit, and let `prefers-reduced-motion` collapse it to none.
- **Do** keep running text at a 38rem measure and let code and tables use the full column.
- **Do** give every copy action visible and announced feedback.

### Don't:
- **Don't** use pure grays or untinted borders.
- **Don't** add shadows to static content containers beyond the rest whisper.
- **Don't** put uppercase tracked labels or small accent-coloured kickers above headings; group labels are 12px semibold sentence case.
- **Don't** fill large surfaces with violet or introduce a second accent hue; status hues live only in callouts and feedback.
- **Don't** enable code ligatures.
- **Don't** exceed weight 600 in the docs surface.
