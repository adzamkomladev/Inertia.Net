# Inertia v3 protocol: implementation reference for Inertia.Net

This is the reference that Inertia.Net follows. It was compiled from:
- the Inertia v3 docs (https://inertiajs.com/docs/v3/core-concepts/the-protocol);
- the `inertiajs/inertia@3.x` client source (`packages/core/src/{types,request,requestParams,response,json,domUtils,ssrUtils,server}.ts`);
- the reference server adapter `inertiajs/inertia-laravel@3.x` (`src/PropsResolver.php`, `Middleware.php`, `Response.php`, `ResponseFactory.php`, `Ssr/HttpGateway.php`, `Testing/AssertableInertia.php`).

When this document and the Laravel adapter disagree, Laravel wins. The exceptions are the deliberate deviations listed at the end.

## 1. Page object

| JSON name | Type | When it is emitted |
|---|---|---|
| `component` | string | always |
| `props` | object | always. Always contains `errors` (defaults to `{}`) |
| `url` | string | always. Path + query relative to host, keeping PathBase and the trailing slash |
| `version` | string | always. `""` when no version is set |
| `sharedProps` | string[] | top-level keys of the shared props, only when non-empty (option `ExposeSharedPropKeys`, default on). `errors` is always shared, so with the option on it is always emitted and lists at least `"errors"` (as in Laravel, whose middleware shares `errors`) |
| `mergeProps` | string[] | dot paths, only when non-empty |
| `prependProps` | string[] | only when non-empty |
| `deepMergeProps` | string[] | only when non-empty |
| `matchPropsOn` | string[] | `"path.field"`, only when non-empty |
| `deferredProps` | `{group: string[]}` | only when non-empty |
| `rescuedProps` | string[] | only when non-empty |
| `scrollProps` | `{path: {pageName, previousPage, nextPage, currentPage, reset}}` | only when non-empty. Page values are int, string or null |
| `onceProps` | `{key: {prop: path, expiresAt: msEpoch or null}}` | only when non-empty |
| `preserveBigIntegers` | `true` | only when true |
| `clearHistory` | `true` | only when true (v3 omits false) |
| `encryptHistory` | `true` | only when true |
| `flash` | object | only when there is flash data |
| `preserveFragment` | `true` | only when true |

**Key order.** Laravel emits keys in this order: component, props, url, version, sharedProps, mergeProps, prependProps, deepMergeProps, matchPropsOn, deferredProps, rescuedProps, scrollProps, onceProps, preserveBigIntegers, clearHistory, encryptHistory, flash, preserveFragment.

**BigInt.** When `preserveBigIntegers` is on, any integer with `|n| > 9007199254740991` is emitted as `{"$bigint":"<digits>"}`.

## 2. Request headers

All list headers are comma-separated. Parse them as: split on `,`, trim, drop empty entries. **A missing or empty header means null**, which means "no filter" and is not the same as an empty list.

| Header | Meaning |
|---|---|
| `X-Inertia: true` | Inertia XHR request |
| `X-Inertia-Version` | Client version. Absent means `""` |
| `X-Inertia-Partial-Component` | A request is partial **only if this equals the component being rendered**. It does not require `X-Inertia` |
| `X-Inertia-Partial-Data` | `only` list |
| `X-Inertia-Partial-Except` | `except` list |
| `X-Inertia-Reset` | `reset` list (defaults to `[]`) |
| `X-Inertia-Error-Bag` | Error bag name |
| `X-Inertia-Except-Once-Props` | Once keys the client already has (defaults to `[]`) |
| `X-Inertia-Infinite-Scroll-Merge-Intent` | `prepend` means prepend; anything else means append |
| `Purpose: prefetch` | Prefetch request |
| `X-XSRF-TOKEN` | Client sends this from the `XSRF-TOKEN` cookie |

## 3. Responses and middleware flow

- `Vary: X-Inertia` goes on **every** response that passes through the middleware.
- A JSON page response has status 200, `Content-Type: application/json`, and `X-Inertia: true`.
- An HTML page response is the root template with the page embedded (§5).

The middleware runs in this order:
1. **Inertia GET with a version mismatch.** If `X-Inertia-Version` (absent = `""`) differs from the current version, respond `409` with `X-Inertia-Location: <absolute request URL>` and `X-Inertia-Version: <current>`, and keep the flash/state. This check runs **before** the handler (deliberate deviation 1). Non-GET requests never get the version 409.
2. Run the handler.
3. If the request is not an Inertia request, return the response unchanged.
4. If the status is 200 and the body is empty, redirect back (same-host Referer, falling back to the app root `PathBase/`).
5. If the status is 302 and the method is PUT, PATCH or DELETE, change it to 303.
6. If the response is a redirect, its `Location` contains `#`, and the request is not a prefetch, replace it with `409` and `X-Inertia-Redirect: <Location>` (empty body).

"Redirect" means status 301, 302, 303, 307 or 308 (Symfony's `isRedirect()`, without 201). When the response is a redirect, or a `409` carrying `X-Inertia-Location`/`X-Inertia-Redirect`, the flash/errors/history flags (set during this request **or** restored and not rendered yet) are saved to the state store; any other response consumes the loaded state (§7).

`Inertia.Location(url)` returns, for an Inertia request, `409` with `X-Inertia-Location: url` and an empty body. For any other request it returns `302 Location: url`.

## 4. Prop resolution: the PropsResolver algorithm (port of Laravel 3.x)

```
ctor:
  isPartial  = header[Partial-Component] == component
  isInertia  = header[X-Inertia] present/truthy
  only       = parseList(Partial-Data)     // null if absent/empty
  except     = parseList(Partial-Except)
  reset      = parseList(Reset) ?? []
  loadedOnce = parseList(Except-Once-Props) ?? []

resolve(shared, props):
  sharedPropKeys = top-level keys of shared (first segment of dotted keys)
  all = shared overlaid with props            // page props override shared at the top level
  all = unpackDotProps(all)                   // top-level "a.b" keys become nested objects
  return (resolveProps(all, "", false), metadata)

resolveProps(props, prefix, parentWasResolved):
  for (key, prop) in props (insertion order):
    path = prefix ? prefix + "." + key : key
    if isPartial and prop is not Always and not parentWasResolved:
        if only != null and not matchesOnly(path) and not leadsToOnly(path): skip
        if except != null and matchesExcept(path): skip            // except beats only
    if not isPartial and excludeFromInitialResponse(prop, path): skip
    value = resolveValue(prop, path)
    if path in rescuedProps: skip
    if value is a different prop type than prop:                  // a loader returned a prop: unwrap once
        prop = value
        if not isPartial and excludeFromInitialResponse(prop, path): skip
        value = resolveValue(prop, path)
    collectMetadata(prop, path)
    result[key] = value is a literal object (dictionary)
        ? resolveProps(value, path, parentWasResolved or prop was not a literal object)
        : value

matchesOnly(p)   = any o in only:   p == o or p.StartsWith(o + ".")
leadsToOnly(p)   = any o in only:   o.StartsWith(p + ".")
matchesExcept(p) = any e in except: p == e or p.StartsWith(e + ".")

excludeFromInitialResponse(prop, path):
  if prop is Optional/Defer (IgnoreFirstLoad):
     if prop is deferred and not wasAlreadyLoadedByClient(prop, path):
         deferredProps[prop.group].append(path)
     if prop merges: collectMergeable(path, prop)
     if prop is once: collectOnce(path, prop)
     return true
  if prop is deferred (e.g. Scroll(...).Defer()):
     deferredProps[group].append(path)
     if prop merges: collectMergeable(path, prop)
     return true
  if isInertia and wasAlreadyLoadedByClient(prop, path):
     collectOnce(path, prop)
     return true
  return false

wasAlreadyLoadedByClient(prop, path) =
  prop is once and not fresh and (prop.key ?? path) in loadedOnce

resolveValue(v, path):
  if v is Scroll: configureMergeIntent(request)   // prepend intent → prepend(wrapper), else append(wrapper)
  try: invoke the loader / unwrap the value
  catch e: if not rescue: throw
           log e; rescuedProps.append(path); return null

collectMetadata(prop, path):
  if prop merges: collectMergeable(path, prop)
  if prop is Scroll: scrollProps[path] = metadata + {reset: path in reset}
  if prop is once: collectOnce(path, prop)

collectMergeable(path, prop):
  if path in reset: return
  if isPartial and not isIncludedInPartialMetadata(path): return
  if deep:                   deepMergeProps.append(path)
  elif append at root:       mergeProps.append(path)        // "at root" = no append/prepend paths
  elif prepend at root:      prependProps.append(path)
  else: for p in appendPaths:  mergeProps.append(path + "." + p)
        for p in prependPaths: prependProps.append(path + "." + p)
  for s in matchOn: matchPropsOn.append(path + "." + s)

collectOnce(path, prop):
  if isPartial and not isIncludedInPartialMetadata(path): return
  onceProps[prop.key ?? path] = {prop: path, expiresAt: ttl ? now + ttl (ms) : null}

isIncludedInPartialMetadata(p) = (only == null or matchesOnly(p)) and (except == null or not matchesExcept(p))
```

**Prop kinds and their flags:**

| Kind | Flags |
|---|---|
| Regular value / lazy `Prop(loader)` | eager; the loader runs only if the prop is included |
| `Always(v)` | bypasses only/except |
| `Optional(loader)` | IgnoreFirstLoad; can be once |
| `Defer(loader, group = "default")` | IgnoreFirstLoad + deferred; can be merge, once, rescue |
| `Merge(v)` / `DeepMerge(v)` | merge = true; can be once. `Append(path?, matchOn?)`, `Prepend(path?, matchOn?)`, `MatchOn(...)` |
| `Once(loader)` | once = true. `As(key)`, `Until(ttl)`, `Fresh()` |
| `Scroll(v, wrapper = "data", metadata)` | merge = true; not deferred unless `.Defer()`; the merge path is `"{path}.{wrapper}"` |

**Semantics worth remembering:**
- Partial reloads skip initial-load exclusion entirely. Once props are re-sent on a partial reload even if the client already has them.
- `deferredProps` is only produced on non-partial renders.
- Nested filtering only applies inside **literal** objects (`InertiaProps`/dictionaries). The children of a value produced by a loader are not filtered.
- `errors` is shared as an Always prop.

## 5. Initial HTML (v3)

```html
<script data-page="app" type="application/json">{page json}</script><div id="app"></div>
```

- The JSON is **not** HTML-entity encoded.
- `<`, `>`, `&` and `/` are escaped as `<`, `>`, `&` and `/` (or `\/`), so `</script>` cannot appear.
- The root id `app` is configurable. The `data-page` value equals the id.

**With SSR,** the SSR `body` already contains the script element plus `<div data-server-rendered="true" id="app">…</div>`. Echo it verbatim and do not emit a second script. The SSR `head` (`string[]`) is joined with `"\n"` and output wherever the `@inertiaHead` token appears.

## 6. Validation errors

- `props.errors` is always present.
- Within each bag, a field maps to its first message, or to all messages when `WithAllErrors` is on.
- `errors` is chosen as follows:
  - no errors stored → `{}`;
  - a `default` bag exists and the `X-Inertia-Error-Bag` header is present → `{ <headerValue>: defaultBagErrors }`;
  - a `default` bag exists and there is no header → the default bag's errors, flattened;
  - otherwise → `{ bag: errors, ... }`.
- The flow: validation fails, the server redirects back (303 for PUT/PATCH/DELETE) with the errors stored in the state store, and the next GET renders them.

## 7. Flash, clearHistory, preserveFragment

All three (and validation errors) are stored in the state store when a response redirects, and are carried through any number of further redirects until a response that is not a redirect consumes them (Laravel re-flashes its flash data on every redirect and keeps the history flags in the session until a page renders).

| Item | How it is set | When it is emitted |
|---|---|---|
| Flash | `Flash(key, value)` | consumed when the next page is rendered; emitted as `page.flash` only when non-empty |
| clearHistory | `ClearHistory()` | `clearHistory: true` on the next rendered page, then the flag is consumed |
| preserveFragment | the same mechanism | `preserveFragment: true` on the next rendered page |
| encryptHistory | global option, or per request | not persisted |

The state is kept (not consumed) when the response is a version-mismatch 409.

## 8. SSR

- **Production:** `POST {ssrUrl}/render` (default `http://127.0.0.1:13714`) with the page JSON as a UTF-8 body. A 200 response is `{"head": string[], "body": string}`.
- **Development** (Vite hot file present): `POST {viteDevUrl}/__inertia_ssr` with the same body.
- **Failure:** the response is `500 {error, type, hint, browserApi?, stack?, sourceLocation?}`. On this, or on a timeout or connection error: log it and fall back to client-side rendering, unless `ThrowOnError` is set.
- **Health:** `GET {ssrUrl}/health` returns 2xx when healthy.
- **Shutdown:** any request to `{ssrUrl}/shutdown` makes the v3 server call `process.exit()` (no response is sent).
- **Port:** the v3 server reads its port and host only from `createServer` options, baked into the bundle by `@inertiajs/vite` (`ssr: { port, host }`, default `13714` / `0.0.0.0`); no CLI argument or environment variable.

## 9. Vite

- **Manifest:** `{outDir}/.vite/manifest.json`, falling back to `{outDir}/manifest.json` (Vite ≤4).
- **Chunk fields:** `file`, `src`, `css[]`, `assets[]`, `isEntry`, `name`, `isDynamicEntry`, `imports[]`, `dynamicImports[]`.
- **Production tags for an entry:**
  1. `<link rel="stylesheet" href>` for the chunk's `css`, then recursively for each static import's `css`, de-duplicated.
  2. `<script type="module" src="/{base}/{file}">`.
  3. `<link rel="modulepreload" href>` for each static import's `file`, recursively and de-duplicated.
- **Development** (hot file contains the dev server origin): emit the React refresh preamble (React only, `@viteReactRefresh`), then `<script type="module" src="{origin}/@vite/client">`, then `<script type="module" src="{origin}/{entry}">`.

The React refresh preamble is:

```html
<script type="module">import RefreshRuntime from '{origin}/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$ = () => {};window.$RefreshSig$ = () => (type) => type;window.__vite_plugin_react_preamble_installed__ = true;</script>
```

## Deliberate deviations from Laravel

1. **The version 409 check runs before the handler**, so no work is wasted rendering a page that will be discarded.
2. **Scroll props:** `configureMergeIntent` runs before metadata is collected for a deferred Scroll prop, so a deferred scroll reports `"posts.data"` rather than root `"posts"`. Laravel 3.x has a quirk here.
3. **Redirect back** uses the `Referer` header only when it has the request's own host and port (no open redirect), falling back to `~/` (the app root, `PathBase` included) or the given fallback. Laravel uses any `Referer`, then the session's previous URL.
4. **Flash, errors and history flags** go through a Data-Protection-encrypted cookie by default (AOT-safe; no Session/TempData requirement).
5. **Validation errors survive chained redirects** like flash data does. Laravel re-flashes only its Inertia flash data, so its `errors` are lost when the redirect target redirects again.
6. **Typed objects given directly are literal objects.** A POCO with `InertiaProp` members passed as a prop (not returned by a loader) keeps partial-reload filtering for its members, like an `InertiaProps` dictionary. Laravel resolves `JsonSerializable`/`Arrayable` objects first, so their children bypass the filter. The client deep-merges nested partial responses, so the result on the page is the same.
7. **Lazy values at dot keys stay lazy.** `"auth.user" = Prop(...)` is unpacked as a prop and only resolved when included; Laravel calls closures at dot keys while unpacking. Intermediate plain lazy props are resolved, as in Laravel's `ensurePathIsTraversable`.
8. **Supersets of Laravel's prop flags.** Any prop can be `Once()`, `Rescue()`d, merged or deferred (Laravel limits e.g. rescue to `DeferProp`), and list headers are trimmed (`"a, b"` works; Laravel does not trim).
9. **SSR returning an empty body falls back to client rendering.** Laravel echoes the empty body.
10. **The Vite hot file is only read in Development** (or use `Vite.DevServerUrl`), so a stale `hot` file deployed to production cannot point pages at a dev server. Laravel reads it in any environment.
