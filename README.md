# Inertia.Net

A .NET 10 server adapter for the [Inertia.js](https://inertiajs.com) **v3** protocol. It works with Minimal APIs, MVC controllers and FastEndpoints, with the React, Vue and Svelte clients, and it is **Native AOT and trim compatible**.

```csharp
app.MapGet("/users", (AppDb db) => Render("Users/Index", new InertiaProps
{
    ["users"] = db.Users.Select(u => new UserDto(u.Id, u.Name)).ToList(),
    ["stats"] = Defer(ct => db.StatsAsync(ct), group: "sidebar"),
    ["plans"] = Once(ct => db.PlansAsync(ct)).Until(TimeSpan.FromHours(1)),
}));
```

- **v3 only.** No v2 compatibility mode. The wire format is documented in [docs/protocol.md](docs/protocol.md).
- **AOT first.** No reflection over your props, one source-generated JSON context for the library's own types. See [Native AOT](#native-aot).
- **Fast.** A 20-prop page renders in about 14 µs and allocates about 2.7 KB. The middleware allocates nothing. See [Performance](#performance).
- **Tested.** Unit tests ported from inertia-laravel 3.x, a protocol conformance suite that runs against all three backends, an AOT smoke test and Playwright end-to-end tests.

## Contents

1. [Overview and feature matrix](#overview-and-feature-matrix)
2. [Installation](#installation)
3. [Quick start](#quick-start): [Minimal API](#minimal-api), [MVC](#mvc), [FastEndpoints](#fastendpoints)
4. [Client setup](#client-setup): [Vite config](#vite-config), [React](#react), [Vue](#vue), [Svelte](#svelte)
5. [The root template](#the-root-template)
6. [Props reference](#props-reference)
7. [Shared data](#shared-data)
8. [Partial reloads](#partial-reloads)
9. [Deferred props](#deferred-props)
10. [Merging props](#merging-props)
11. [Once props](#once-props)
12. [Infinite scroll](#infinite-scroll)
13. [Validation and error bags](#validation-and-error-bags) and [Precognition](#precognition-live-validation)
14. [Flash data](#flash-data)
15. [Redirects, Location and Back](#redirects-location-and-back)
16. [History encryption, clearing and fragments](#history-encryption-clearing-and-fragments)
17. [Asset versioning](#asset-versioning)
18. [Server-side rendering](#server-side-rendering)
19. [Vite integration](#vite-integration)
20. [CSRF and antiforgery](#csrf-and-antiforgery)
21. [Native AOT](#native-aot)
22. [Testing](#testing)
23. [Performance](#performance)
24. [Configuration reference](#configuration-reference)
25. [Migrating from other .NET adapters](#migrating-from-other-net-adapters)
26. [Troubleshooting and FAQ](#troubleshooting-and-faq)
27. [Contributing and license](#contributing-and-license)

---

## Overview and feature matrix

| | Minimal API | MVC controllers | FastEndpoints |
|---|:---:|:---:|:---:|
| Package | `Inertia.Net` | `Inertia.Net` + `Inertia.Net.Mvc` | `Inertia.Net` + `Inertia.Net.FastEndpoints` |
| Render / Back / Location | `Render(...)` returns an `IResult` | same, return `IResult` from an action | `Send.InertiaAsync(...)` or return the result |
| Validation redirect with errors | `.WithInertiaValidation()` | `ModelState` filter (global) | `c.UseInertia()` pre-processor |
| Precognition | `.WithInertiaPrecognition()` | not available | `c.UseInertia()` |
| Razor root view | no (`app.html` template) | optional (tag helpers) | no (`app.html` template) |
| Native AOT | yes | no (MVC itself is not AOT) | yes (with the FastEndpoints source generator) |

| Client | Package | Supported |
|---|---|:---:|
| React | `@inertiajs/react` v3 | yes |
| Vue | `@inertiajs/vue3` v3 | yes |
| Svelte | `@inertiajs/svelte` v3 | yes |

### Protocol features

- [x] Page object: `component`, `props`, `url`, `version`; JSON responses and the initial `<script data-page="app" type="application/json">` payload
- [x] Asset versioning with a `409` + `X-Inertia-Location` (checked before your handler runs)
- [x] `Vary: X-Inertia`, 302 to 303 for PUT/PATCH/DELETE, fragment redirects (`X-Inertia-Redirect`), `Location()`, `Back()`
- [x] Partial reloads (`only`, `except`, nested dot paths), `Always`, `Optional`, lazy loaders
- [x] Deferred props with groups (`deferredProps`)
- [x] Merge props: append, prepend, deep merge, `matchPropsOn`, reset (`mergeProps`, `prependProps`, `deepMergeProps`)
- [x] Once props with keys, expiry and `fresh` (`onceProps`, `X-Inertia-Except-Once-Props`)
- [x] Infinite scroll (`scrollProps`, merge intent header)
- [x] Rescued props (`rescuedProps`)
- [x] Shared props and `sharedProps`
- [x] Validation errors, error bags, `WithAllErrors`
- [x] Precognition (live validation)
- [x] Flash data (`flash`)
- [x] `encryptHistory`, `clearHistory`, `preserveFragment`, `preserveBigIntegers`
- [x] Server-side rendering (production Node server, Vite dev server, health check)
- [x] Vite manifest and dev-server tags, CDN asset base URL

Not in 1.0: spawning and managing the Node SSR process (run it with Docker or a process manager), v2 protocol compatibility, a TempData state store.

---

## Installation

| Package | Install when |
|---|---|
| `Inertia.Net` | always. The core adapter, Minimal API support, Vite, SSR, state store, antiforgery cookie |
| `Inertia.Net.Mvc` | you use MVC controllers: `ModelState` to errors filter, Razor root view and tag helpers |
| `Inertia.Net.FastEndpoints` | you use FastEndpoints: `Send` extensions, validation pre-processor, Precognition |
| `Inertia.Net.Testing` | in your test project: fluent assertions over Inertia responses |

```sh
dotnet add package Inertia.Net
dotnet add package Inertia.Net.Mvc              # MVC only
dotnet add package Inertia.Net.FastEndpoints    # FastEndpoints only
dotnet add package Inertia.Net.Testing          # test projects
```

Requires .NET 10 and an ASP.NET Core app (`Microsoft.NET.Sdk.Web`).

> **Callout: `Render(...)`, `Defer(...)`, `Back()` without a prefix.**
> The namespace is `Inertia.Net` and the static factory class is `Inertia.Net.Inertia`. C# resolves a leading `Inertia` to the *namespace*, so `Inertia.Render(...)` does **not** compile in your app (outside the `Inertia.Net` namespace).
> The package's `buildTransitive` props file therefore adds `global using Inertia.Net;` and `global using static Inertia.Net.Inertia;` to your project when `<ImplicitUsings>enable</ImplicitUsings>` is set (the default in new projects). Write `Render(...)`, `Defer(...)`, `Once(...)`, `Back()`, `Location(...)` unqualified.
> To opt out set `<InertiaNetImplicitUsings>false</InertiaNetImplicitUsings>` and add `using static Inertia.Net.Inertia;` yourself, or call `Inertia.Net.Inertia.Render(...)`.
> `HttpContext.Inertia()` (per-request flash, errors, history flags) is an extension method and works as written.

---

## Quick start

Every backend needs the same four things: `AddInertia()`, `UseInertia()`, a root template, and a frontend (see [Client setup](#client-setup)).

The root template is an HTML file in the content root. The default name is `app.html`:

```html
<!-- app.html -->
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>My app</title>
  @viteReactRefresh
  @vite("resources/js/app.tsx")
  @inertiaHead
</head>
<body>
  @inertia
</body>
</html>
```

Remove `@viteReactRefresh` for Vue and Svelte. All tokens are described in [The root template](#the-root-template).

Make sure `app.html` is copied to the output and publish directory:

```xml
<ItemGroup>
  <Content Include="app.html" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

### Minimal API

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();                    // .NET 10 source-generated validation
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Demo");
});

var app = builder.Build();

app.UseStaticFiles();                                // serves wwwroot/build (Vite output)
app.UseInertia();                                    // before the endpoints

app.MapGet("/", () => Render("Home", new InertiaProps
{
    ["greeting"] = "Hello from .NET",
}));

app.MapPost("/users", (CreateUserInput input, AppDb db) =>
{
    db.Add(input);
    return Results.Redirect("/users");               // 302 becomes 303 for PUT/PATCH/DELETE
}).WithInertiaValidation();                          // invalid input: redirect back with errors

app.Run();

public sealed class CreateUserInput                  // must be public for AddValidation()
{
    [Required] public string? Name { get; set; }
    [Required, EmailAddress] public string? Email { get; set; }
}
```

### MVC

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInertia();
builder.Services.AddControllersWithViews().AddInertiaMvc();   // add .UseRazorRootView() options here if you want Razor

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();
app.MapControllers();
app.Run();
```

```csharp
using Microsoft.AspNetCore.Mvc;

public sealed class UsersController(AppDb db) : Controller
{
    [HttpGet("/users")]
    public IResult Index() => Render("Users/Index", new InertiaProps
    {
        ["users"] = db.Users.ToList(),
    });

    [HttpPost("/users")]
    public IActionResult Store([FromBody] CreateUserInput input)     // the Inertia client posts JSON
    {
        // An Inertia request with an invalid ModelState never reaches this point:
        // the filter already redirected back with the errors.
        db.Add(input);
        return Redirect("/users");
    }
}
```

Controllers return the core `IResult` factories (`Render`, `Back`, `Location`); MVC executes any `IResult`. `Inertia.Net.Mvc` adds the `ModelState` filter and the optional Razor root view. See [Validation](#validation-and-error-bags).

> Prop serialization uses the **HTTP** JSON options (`ConfigureHttpJsonOptions`), not the MVC `JsonOptions`, even in an MVC app. Configure naming and converters for Inertia props with `builder.Services.ConfigureHttpJsonOptions(...)`.

### FastEndpoints

```csharp
// Program.cs
using FastEndpoints;
using Inertia.Net.FastEndpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInertia();
builder.Services.AddFastEndpoints();

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();
app.UseFastEndpoints(c => c.UseInertia());           // validation redirect + Precognition
app.Run();
```

```csharp
using FastEndpoints;
using Inertia.Net.FastEndpoints;

public sealed class UsersEndpoint(AppDb db) : EndpointWithoutRequest
{
    public override void Configure() => Get("/users");

    public override Task HandleAsync(CancellationToken ct) =>
        Send.InertiaAsync("Users/Index", new InertiaProps { ["users"] = db.Users.ToList() });
}

// Or return the result from ExecuteAsync:
public sealed class HomeEndpoint : EndpointWithoutRequest<InertiaResult>
{
    public override void Configure() => Get("/");
    public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Render("Home"));
}
```

`Send.InertiaAsync`, `Send.InertiaLocationAsync` and `Send.InertiaBackAsync` are the `Send` extensions. For errors or flash on a redirect back, send `Back().WithErrors(...).WithFlash(...)` with `Send.ResultAsync(...)`.

---

## Client setup

Inertia.Net speaks the v3 protocol: the initial page is the `<script data-page="app" type="application/json">` element, which the v3 clients read directly. Install the v3 client packages for your framework:

```sh
npm install react react-dom @vitejs/plugin-react @inertiajs/react          # React
npm install vue @vitejs/plugin-vue @inertiajs/vue3                          # Vue
npm install svelte @sveltejs/vite-plugin-svelte @inertiajs/svelte           # Svelte
npm install -D vite typescript
```

The examples keep the frontend in `resources/js` and the Vite output in `wwwroot/build`. Pages live in `resources/js/Pages`, so the component `Users/Index` is `resources/js/Pages/Users/Index.tsx`.

### Vite config

```ts
// vite.config.ts
import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'            // Vue: @vitejs/plugin-vue, Svelte: @sveltejs/vite-plugin-svelte
import { mkdirSync, rmSync, writeFileSync } from 'node:fs'

// Writes the dev server URL to wwwroot/hot. Inertia.Net reads it (in Development only) and then renders the
// dev-server tags instead of the manifest tags. Alternative: set o.Vite.DevServerUrl in AddInertia (see "Vite integration").
function hotFile(): Plugin {
  const file = 'wwwroot/hot'
  return {
    name: 'inertia-net-hot-file',
    apply: 'serve',
    configureServer(server) {
      server.httpServer?.once('listening', () => {
        const address = server.httpServer!.address()
        if (address && typeof address === 'object') {
          mkdirSync('wwwroot', { recursive: true })
          writeFileSync(file, `http://localhost:${address.port}`)
        }
      })
      const clean = () => rmSync(file, { force: true })
      process.on('exit', clean)
      process.on('SIGINT', () => process.exit())
      process.on('SIGTERM', () => process.exit())
    },
  }
}

export default defineConfig(({ command }) => ({
  plugins: [react(), hotFile()],
  // Built assets are served from /build/. The dev server must serve from the root, because Inertia.Net emits
  // {origin}/@vite/client and {origin}/{entry}.
  base: command === 'build' ? '/build/' : '/',
  server: { port: 5173, strictPort: true, origin: 'http://localhost:5173' },
  build: {
    outDir: 'wwwroot/build',                         // matches ViteOptions.PublicDirectory + BuildDirectory
    emptyOutDir: true,
    manifest: true,                                  // wwwroot/build/.vite/manifest.json
    rollupOptions: { input: 'resources/js/app.tsx' },
  },
}))
```

`package.json` scripts:

```json
{ "type": "module", "scripts": { "dev": "vite", "build": "vite build" } }
```

The manifest key of the entry (`resources/js/app.tsx`) is what you pass to `@vite("...")`.

The optional `@inertiajs/vite` plugin can resolve pages and mount the app for you (`createInertiaApp()` with no arguments). The entries below spell everything out so they work with or without it. Check the [Inertia v3 client docs](https://inertiajs.com/docs/v3/installation/client-side-setup) for the plugin's options.

### React

```tsx
// resources/js/app.tsx
import { createInertiaApp } from '@inertiajs/react'
import { createRoot } from 'react-dom/client'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob('./Pages/**/*.tsx', { eager: true })
    return pages[`./Pages/${name}.tsx`]
  },
  setup({ el, App, props }) {
    createRoot(el).render(<App {...props} />)
  },
})
```

```tsx
// resources/js/Pages/Users/Index.tsx
import { Link } from '@inertiajs/react'

export default function Index({ users }: { users: { id: number; name: string }[] }) {
  return (
    <ul>
      {users.map((u) => (
        <li key={u.id}><Link href={`/users/${u.id}`}>{u.name}</Link></li>
      ))}
    </ul>
  )
}
```

### Vue

```ts
// resources/js/app.ts
import { createApp, h } from 'vue'
import { createInertiaApp } from '@inertiajs/vue3'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob('./Pages/**/*.vue', { eager: true })
    return pages[`./Pages/${name}.vue`]
  },
  setup({ el, App, props, plugin }) {
    createApp({ render: () => h(App, props) }).use(plugin).mount(el)
  },
})
```

Use `@vite("resources/js/app.ts")` in the template and `input: 'resources/js/app.ts'` in the Vite config. Remove `@viteReactRefresh`.

```vue
<!-- resources/js/Pages/Users/Index.vue -->
<script setup lang="ts">
import { Link } from '@inertiajs/vue3'
defineProps<{ users: { id: number; name: string }[] }>()
</script>

<template>
  <ul><li v-for="u in users" :key="u.id"><Link :href="`/users/${u.id}`">{{ u.name }}</Link></li></ul>
</template>
```

### Svelte

```ts
// resources/js/app.ts
import { mount } from 'svelte'
import { createInertiaApp } from '@inertiajs/svelte'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob('./Pages/**/*.svelte', { eager: true })
    return pages[`./Pages/${name}.svelte`]
  },
  setup({ el, App, props }) {
    mount(App, { target: el, props })
  },
})
```

```svelte
<!-- resources/js/Pages/Users/Index.svelte -->
<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  let { users }: { users: { id: number; name: string }[] } = $props()
</script>

<ul>{#each users as u (u.id)}<li><Link href={`/users/${u.id}`}>{u.name}</Link></li>{/each}</ul>
```

### Development workflow

```sh
npm run dev          # terminal 1: Vite on :5173, writes wwwroot/hot
dotnet watch         # terminal 2: the app, ASPNETCORE_ENVIRONMENT=Development
```

In production run `npm run build`, then deploy `wwwroot/build` with the app. Samples for each framework live in [samples/](samples/).

---

## The root template

The default root view parses an HTML file at startup (and re-reads it on change in Development) into literal chunks plus tokens. It throws at startup when `@inertia` is missing. The path is `InertiaOptions.RootView` (default `app.html`) relative to the content root.

| Token | Output |
|---|---|
| `@inertia` | Without SSR: `<script data-page="app" type="application/json">{page}</script><div id="app"></div>`. With SSR: the server-rendered body, verbatim (it already contains the script element). **Required.** |
| `@inertiaHead` | The SSR `head` tags joined with `\n`. Empty without SSR. |
| `@vite("resources/js/app.tsx")` | The Vite tags for one or more comma-separated entries: `@vite("a.ts", "b.css")`. See [Vite integration](#vite-integration). |
| `@viteReactRefresh` | The React Fast Refresh preamble while the dev server runs; empty otherwise. Put it before `@vite`. |
| `@viewData("title")` | An HTML-encoded value passed with `Render(...).WithViewData("title", "Dashboard")`. Empty when absent. |
| `@@` | A literal `@`. |

Other `@` characters (CSS at-rules, e-mail addresses) are left alone. Arguments use single or double quotes.

The page JSON is not HTML-entity encoded. `<`, `>`, `&` and `/` are written as `\uXXXX` escapes, so `</script>` can never break out, whatever your `JavaScriptEncoder` setting is.

Per-response overrides:

```csharp
Render("Admin/Home").WithRootView("admin.html").WithViewData("title", "Admin");
Render("Errors/NotFound").WithStatusCode(404);
```

`InertiaOptions.RootElementId` (default `app`) changes both the root `<div id>` and the `data-page` value. The client finds the page script by that id.

### Razor alternative (MVC)

With `Inertia.Net.Mvc` the first-visit HTML can be a Razor view with tag helpers:

```csharp
builder.Services.AddControllersWithViews().AddInertiaMvc(o => o.UseRazorRootView("App"));
```

`"App"` finds `Views/Shared/App.cshtml`, or pass an app-relative path such as `"~/Views/App.cshtml"`. The view receives an `InertiaRootViewContext` model:

```cshtml
@model InertiaRootViewContext
<!DOCTYPE html>
<html>
<head>
    <title>@ViewData["title"]</title>
    <vite-react-refresh />
    <vite entry="resources/js/app.tsx" />
    <inertia-head />
</head>
<body>
    <inertia />
</body>
</html>
```

```cshtml
@* Views/_ViewImports.cshtml *@
@addTagHelper *, Inertia.Net.Mvc
```

`<vite entry="a.ts, b.css" />` takes comma-separated entries. SSR runs at most once per request, shared by `<inertia-head />` and `<inertia />`. Data from `WithViewData` is in `ViewData`. `WithRootView("Other")` takes Razor view names when the Razor root view is on.

### A custom root view

Implement `IInertiaRootView` (it receives the buffered page JSON and writes the response). Register it before `AddInertia`, which uses `TryAdd`:

```csharp
builder.Services.AddSingleton<IInertiaRootView, MyRootView>();
builder.Services.AddInertia();

public sealed class MyRootView : IInertiaRootView
{
    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        var writer = context.HttpContext.Response.BodyWriter;
        writer.Write("<!DOCTYPE html><html><body>"u8);
        context.WriteBody(writer);                   // or context.GetBodyHtml()
        writer.Write("</body></html>"u8);
        await writer.FlushAsync(context.HttpContext.RequestAborted);
    }
}
```

---

## Props reference

`Render(component, props)` takes either an `InertiaProps` (an ordered `Dictionary<string, object?>`), any `IDictionary<string, object?>`, or a typed object whose properties are the props. Nested `InertiaProps` and dictionaries are walked, so props can be nested at any depth.

Every page has an `errors` prop (an empty object when there are no errors). Key order in `props` is `errors`, shared props, then the page props.

### The prop factories

| C# | Behavior |
|---|---|
| `["title"] = "Hi"` | A plain value. Sent on every response that includes the key. |
| `Prop(() => ...)` | Lazy: the loader runs only when the prop is part of the response. |
| `Always(value)` or `Always(() => ...)` | Sent even when a partial reload does not ask for it. |
| `Optional(() => ...)` | Never sent on the first visit, only when a partial reload asks for it. |
| `Defer(() => ..., group: "name")` | Left out of the first response. The client loads it right away, together with the other props of its group. |
| `Merge(value)` | The client appends the new value to its current one. Chain `.Prepend()`, `.Append(path, matchOn)`, `.MatchOn(...)`. |
| `DeepMerge(value)` | The client deep merges the value. |
| `Once(() => ...)` | Resolved once and remembered by the client. Chain `.Until(ttl)`, `.As(key)`, `.Fresh()`. |
| `Scroll(page, wrapper: "data", metadata)` | An infinite-scroll prop: merged at `{path}.{wrapper}` plus paging metadata. |

Every lazy factory (`Prop`, `Always`, `Optional`, `Defer`, `Merge`, `DeepMerge`, `Once`, `Scroll`) accepts five loader shapes:

```csharp
Prop(() => value)                                  // Func<T>
Prop(() => repo.GetAsync())                        // Func<Task<T>>
Prop(() => repo.GetValueTaskAsync())               // Func<ValueTask<T>>
Prop(ct => repo.GetAsync(ct))                      // Func<CancellationToken, Task<T>>
Prop(ct => repo.GetValueTaskAsync(ct))             // Func<CancellationToken, ValueTask<T>>
```

The token is `HttpContext.RequestAborted`. Overloads bind so that an `async` lambda always means the awaiting overload, never "a prop whose value is a Task".

Fluent methods on every prop (they mutate and return the same instance): `.Once(key?, until?)`, `.As(key)`, `.Until(ttl)`, `.Fresh()`, `.Append(path?, matchOn?)`, `.Prepend(path?, matchOn?)`, `.DeepMerge()`, `.MatchOn(params keys)`, `.Defer(group)`, `.WithGroup(group)`, `.Rescue()`.

**Loaders run sequentially**, in prop order, and only when their prop is included in the response. That makes a scoped EF Core `DbContext` safe to use in several loaders. A loader that returns another prop (for example `Prop(() => Defer(...))`) is unwrapped once.

The page is buffered before it is sent, so a loader that throws never produces a half-written 200.

### What each prop produces

The fragments below show the relevant part of the page object. They leave out `sharedProps`, which always lists at least `"errors"` (see [Shared data](#shared-data)).

**Plain, lazy and always props**

```csharp
Render("Users/Index", new InertiaProps
{
    ["title"] = "Users",
    ["users"] = Prop(ct => db.Users.ToListAsync(ct)),
    ["filters"] = Always(new { search = "ann" }),
});
```

```json
{
  "component": "Users/Index",
  "props": { "errors": {}, "title": "Users", "users": [ { "id": 1, "name": "Ann" } ], "filters": { "search": "ann" } },
  "url": "/users?search=ann",
  "version": "3f9a1c0d5b7e2a64c8d1e0f9b3a57c21",
  "sharedProps": ["errors"]
}
```

**Optional**

`["permissions"] = Optional(() => ...)` is absent from the first response and from other partial reloads. A partial reload with `X-Inertia-Partial-Data: permissions` includes it.

**Deferred**

```csharp
["stats"] = Defer(ct => stats.GetAsync(ct)),                 // group "default"
["feed"]  = Defer(ct => feed.GetAsync(ct), group: "sidebar"),
```

```json
{ "props": { "errors": {} }, "deferredProps": { "default": ["stats"], "sidebar": ["feed"] } }
```

**Merge, prepend, deep merge, match on**

```csharp
["posts"]    = Merge(posts),                                  // append
["messages"] = Prop(() => messages).Prepend(),                // prepend
["settings"] = DeepMerge(settings),
["rows"]     = Merge(rows).MatchOn("id"),                     // replace items with the same id
["chat"]     = Merge(chat).Append("messages", matchOn: "id"), // append at a nested path
```

```json
{
  "mergeProps": ["posts", "chat.messages"],
  "prependProps": ["messages"],
  "deepMergeProps": ["settings"],
  "matchPropsOn": ["rows.id", "chat.messages.id"]
}
```

**Once**

```csharp
["plans"] = Once(ct => plans.AllAsync(ct)).Until(TimeSpan.FromHours(1)).As("billing-plans"),
```

```json
{
  "props": { "errors": {}, "plans": [ { "id": "pro" } ] },
  "onceProps": { "billing-plans": { "prop": "plans", "expiresAt": 1760000000000 } }
}
```

`expiresAt` is milliseconds since the Unix epoch, or `null` without `.Until(...)`. When the client already holds the key it sends `X-Inertia-Except-Once-Props` and the value is left out while the `onceProps` entry stays.

**Scroll**

```csharp
["feed"] = Scroll(page, wrapper: "data", metadata: ScrollMetadata.FromPage(2, hasMore: true)),
```

```json
{
  "props": { "errors": {}, "feed": { "data": [ { "id": 11 } ] } },
  "mergeProps": ["feed.data"],
  "scrollProps": { "feed": { "pageName": "page", "previousPage": 1, "nextPage": 3, "currentPage": 2, "reset": false } }
}
```

**Rescue**

```csharp
["recommendations"] = Defer(ct => recs.GetAsync(ct)).Rescue(),
```

If the loader throws, the exception is logged and the page is still sent. The prop is left out of `props` and its path goes to `"rescuedProps": ["recommendations"]`. Without `.Rescue()` the exception propagates normally. Cancellation of the request is never rescued.

**Typed page props**

A typed object is AOT-friendly: it is walked through its `JsonTypeInfo`, and members of type `InertiaProp` are resolved by Inertia.Net while the rest is serialized normally. Property names follow the JSON naming policy (camelCase by default).

```csharp
public sealed class UsersPage
{
    public required IReadOnlyList<UserDto> Users { get; init; }
    public required InertiaProp Stats { get; init; }        // resolved by Inertia.Net
}

app.MapGet("/users", (AppDb db) => Render("Users/Index", new UsersPage
{
    Users = db.Users.Select(u => new UserDto(u.Id, u.Name)).ToList(),
    Stats = Defer(ct => db.StatsAsync(ct)),
}));
```

Register `UsersPage` and its value types in your `JsonSerializerContext` (see [Native AOT](#native-aot)). Anonymous types (`new { users }`) work only when reflection-based serialization is enabled, so not under AOT.

**Dot keys.** A top-level key containing dots is unpacked: `["user.name"] = "Ann"` becomes `"user": { "name": "Ann" }`.

**BigInteger safety.** With `o.PreserveBigIntegers = true`, integers beyond ±(2^53 − 1) are written as `{"$bigint":"9007199254740993"}` and the page gets `"preserveBigIntegers": true`.

---

## Shared data

Shared props are merged into every page. A page prop with the same key wins.

```csharp
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Demo");                                                    // static value
    o.Share("auth", ctx => new AuthInfo(ctx.User.Identity?.Name));                // per request: Func<HttpContext, object?>
});
```

Per request, from anywhere with an `HttpContext` (middleware, a handler):

```csharp
app.Use((context, next) =>
{
    context.Inertia().Share("locale", CultureInfo.CurrentUICulture.Name);
    return next(context);
});
```

Shared values can be any prop: `o.Share("permissions", ctx => Once(() => LoadPermissions(ctx)))` works too. The top-level shared keys are listed in `"sharedProps": ["errors", "appName", "auth"]` so the client can keep them across navigations. Turn that list off with `o.ExposeSharedPropKeys = false`.

---

## Partial reloads

The client sends `X-Inertia-Partial-Component`, and `X-Inertia-Partial-Data` (`only`) or `X-Inertia-Partial-Except` (`except`). The server then filters props:

- Filtering applies only when the partial component equals the component being rendered.
- Paths are dot paths and match on a `.` boundary: `only: ["user"]` includes `user.name`, and `only: ["user.name"]` includes `user.name` and keeps `user` as a container.
- `except` beats `only`. `Always(...)` ignores both.
- The children of a value produced by a loader are not filtered. Nested literal `InertiaProps` are.
- Loaders of excluded props never run.
- A missing or empty list header means "no filter", which differs from an empty list.

```tsx
router.reload({ only: ['users'] })
```

```csharp
["users"] = Prop(ct => db.Users.ToListAsync(ct)),    // runs on the partial reload above
["roles"] = Prop(ct => db.Roles.ToListAsync(ct)),    // does not run
```

---

## Deferred props

`Defer` leaves the prop out of the first response and lists it under `deferredProps`. The client requests each group with a partial reload as soon as the page is mounted.

```csharp
return Render("Dashboard", new InertiaProps
{
    ["user"] = user,
    ["stats"] = Defer(ct => reports.StatsAsync(ct)),
    ["chart"] = Defer(ct => reports.ChartAsync(ct), group: "charts"),
    ["activity"] = Defer(ct => reports.ActivityAsync(ct), group: "charts"),
});
```

Props in the same group load in one request. Different groups load in parallel requests. `deferredProps` is only produced on non-partial renders. A deferred prop can also be `Merge`, `Once` or `.Rescue()`.

```tsx
import { Deferred } from '@inertiajs/react'

<Deferred data="stats" fallback={<Spinner />}><Stats /></Deferred>
```

---

## Merging props

By default a reloaded prop replaces the client's value. Merge props tell the client to combine them.

| C# | Metadata | Client behavior |
|---|---|---|
| `Merge(v)` | `mergeProps: ["key"]` | append to the existing array |
| `Prop(...).Prepend()` | `prependProps: ["key"]` | prepend |
| `Merge(v).Append("path")` | `mergeProps: ["key.path"]` | append at a nested path |
| `Prop(...).Prepend("path")` | `prependProps: ["key.path"]` | prepend at a nested path |
| `DeepMerge(v)` | `deepMergeProps: ["key"]` | deep merge objects and arrays |
| `.MatchOn("id")` | `matchPropsOn: ["key.id"]` | replace items that have the same `id` instead of duplicating them |
| `.Append("path", matchOn: "id")` | `matchPropsOn: ["key.path.id"]` | the match key is relative to the nested path |

Metadata is skipped for paths listed in `X-Inertia-Reset` (the client wants a fresh start), and on partial reloads for paths the reload did not ask for.

```csharp
["posts"] = Merge(ct => db.Posts.Page(page).ToListAsync(ct)).MatchOn("id"),
```

---

## Once props

Once props are expensive values that rarely change. The client remembers them across visits and tells the server which keys it holds (`X-Inertia-Except-Once-Props`).

```csharp
["countries"] = Once(ct => db.CountriesAsync(ct)),                                  // key = the prop path
["plans"]     = Once(ct => db.PlansAsync(ct)).As("billing-plans"),                   // custom key
["rates"]     = Once(ct => fx.RatesAsync(ct)).Until(TimeSpan.FromMinutes(10)),       // client expires it
["tax"]       = Once(ct => db.TaxAsync(ct)).Fresh(),                                 // always re-send
["flags"]     = Prop(ct => db.FlagsAsync(ct)).Once(key: "flags", until: TimeSpan.FromHours(1)),
```

- The value is left out on later visits, but the `onceProps` entry stays so the client keeps its copy.
- `.Fresh()` sends the value even when the client has it.
- Partial reloads always resolve once props that the reload asks for.
- Expiry is computed from the registered `TimeProvider`, so tests can use `FakeTimeProvider`.
- `.As(key)` and `.Until(ttl)` imply once, and sharing a key between pages lets the client reuse one value across them.

---

## Infinite scroll

`Scroll(...)` merges the items inside the value (the *wrapper*, `"data"` by default) and reports paging metadata to the `<InfiniteScroll>` client component.

```csharp
public sealed record FeedPage(IReadOnlyList<PostDto> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);   // pageName defaults to "page"
}

app.MapGet("/feed", async (int? page, AppDb db, CancellationToken ct) =>
{
    var p = page ?? 1;
    var items = await db.Posts.OrderByDescending(x => x.Id).Skip((p - 1) * 20).Take(21).ToListAsync(ct);
    return Render("Feed", new InertiaProps
    {
        ["posts"] = Scroll(new FeedPage(items.Take(20).ToList(), p, items.Count > 20)),
    });
});
```

- `ScrollMetadata(PageName, PreviousPage, NextPage, CurrentPage)` is a record. Page values can be `int`, `long`, `string` or `null` (cursor paging uses strings).
- Metadata comes from the `metadata:` argument, or else from the value if it implements `IProvidesScrollMetadata`. Without either, the render throws with a message naming the prop.
- The client sends `X-Inertia-Infinite-Scroll-Merge-Intent: prepend` when scrolling up. The wrapper is then reported under `prependProps` instead of `mergeProps`.
- `scrollProps[path].reset` is true when the request lists the path in `X-Inertia-Reset`.
- `Scroll(...)` accepts the five loader shapes, and `.Defer()` / `.Once()` / `.Rescue()` combine with it. A deferred scroll prop reports `"posts.data"` just like a resolved one (a deliberate fix of an inertia-laravel quirk).

```tsx
import { InfiniteScroll } from '@inertiajs/react'

<InfiniteScroll data="posts">{posts.data.map((p) => <Post key={p.id} post={p} />)}</InfiniteScroll>
```

---

## Validation and error bags

Inertia forms post, expect a redirect, and read validation errors from `props.errors` on the next page. Inertia.Net does the redirect and carries the errors across it (see [Flash data](#flash-data) for how state is stored).

- A field maps to its **first** message. With `o.WithAllErrors = true` it maps to an array of all messages (the client's `form.withAllErrors()` expects that).
- Error keys are the JSON names of the model members, one naming-policy conversion per segment: `Address.Street` becomes `address.street`. (`[JsonPropertyName]` overrides are not applied to these keys.)
- **Error bags.** The client sends `X-Inertia-Error-Bag: createUser` when the form has `errorBag="createUser"`. Errors then arrive as `errors: { createUser: { email: "..." } }`. Without the header, `errors` is the flat object `{ email: "..." }`. A bag stored under a name other than `default` is always sent nested by bag name.

```ts
router.post('/users', data, { errorBag: 'createUser' })
```

### Minimal API

`WithInertiaValidation()` for a route or a route group. Requires `services.AddValidation()` for attribute validation.

```csharp
builder.Services.AddValidation();

app.MapPost("/users", (CreateUserInput input, AppDb db) =>
{
    db.Add(input);
    return Results.Redirect("/users");
}).WithInertiaValidation();

app.MapGroup("/admin").WithInertiaValidation().MapPost("/roles", (RoleInput input) => Results.Redirect("/admin/roles"));
```

For an Inertia request that is not a GET, a failure becomes a redirect back (303 for PUT/PATCH/DELETE) with the errors. Every other client still gets the normal `400` validation problem. A handler that returns `TypedResults.ValidationProblem(...)`, `Results.ValidationProblem(...)` or an `HttpValidationProblemDetails` is converted the same way.

**Why not just rely on `AddValidation()`?** The built-in .NET 10 validation filter is added automatically and always runs outermost: it writes the 400 itself before any filter you add can see it, so it cannot be wrapped. `WithInertiaValidation()` disables it on the endpoint and runs the same source-generated validation itself. Do not combine it with your own `.DisableValidation()` call.

To report errors yourself, with no attribute validation:

```csharp
app.MapPost("/login", (LoginInput input, SignInService auth) =>
    auth.TryLogin(input)
        ? Results.Redirect("/")
        : Back().WithErrors(new Dictionary<string, string> { ["email"] = "These credentials do not match." }));
```

`Back().WithErrors(IDictionary<string, string[]> errors, string bag = "default")` also takes arrays of messages.

### MVC

`AddInertiaMvc()` registers a global filter: an Inertia non-GET request with an invalid `ModelState` is redirected back with the errors before the action runs, in the bag from `X-Inertia-Error-Bag` (default `default`). `[ApiController]` controllers work too: their automatic 400 is replaced by the same redirect for Inertia requests. Non-Inertia requests keep the standard behavior.

```csharp
[HttpPost("/users")]
public IActionResult Store([FromBody] CreateUserInput input)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);   // reached by non-Inertia clients only
    db.Add(input);
    return Redirect("/users");
}
```

Binding failures get the generic message `The value is invalid.`, because their exception messages can expose internals.

### FastEndpoints

`c.UseInertia()` adds a global pre-processor. A validation failure of an Inertia non-GET request becomes a redirect back with the errors; other requests keep FastEndpoints' 400 JSON. FluentValidation validators work as usual.

```csharp
public sealed class CreateUserValidator : Validator<CreateUserRequest>
{
    public CreateUserValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

app.UseFastEndpoints(c => c.UseInertia(ep => ep.AllowAnonymous()));   // the argument is your own per-endpoint configuration
```

`c.UseInertia()` sets `c.Endpoints.Configurator`. Pass your own configuration as its argument instead of setting the configurator separately. The pre-processor runs after an endpoint's own pre-processors, so one that already answered (a 403, say) wins.

### Precognition (live validation)

Precognition validates a form as the user types, without running the handler. A request with `Precognition: true` is validated and answered with:

- `204` and `Precognition-Success: true` when valid;
- `422` and `{ "message": "...", "errors": { "field": ["..."] } }` when not, limited to the fields named in `Precognition-Validate-Only`;
- `Precognition: true` and `Vary: Precognition` on every response.

```csharp
// Minimal API: use it instead of WithInertiaValidation(), not in addition.
app.MapPost("/users", (CreateUserInput input) => Results.Redirect("/users")).WithInertiaPrecognition();

// FastEndpoints: nothing to add, c.UseInertia() already answers Precognition requests.
```

`WithInertiaPrecognition()` is a superset of `WithInertiaValidation()`: normal Inertia validation redirects keep working. Any endpoint filter you add after it does not run for a precognitive request. In MVC, Precognition is not available.

Client (Inertia v3 has it built in):

```tsx
const form = useForm({ name: '', email: '' }).withPrecognition('post', '/users')
// <input onBlur={() => form.validate('email')} />
```

---

## Flash data

Flash data is carried across the redirect (and any further redirects) and consumed by the next response that is not a redirect. It appears as `page.flash`, only when there is some.

```csharp
app.MapPost("/users", (CreateUserInput input, HttpContext context) =>
{
    context.Inertia().Flash("toast", new { type = "success", message = "Saved" });   // see the AOT note for typed values
    return Results.Redirect("/users");
});

// or on Back():
return Back().WithFlash("toast", "Saved");
```

```json
{ "component": "Users/Index", "props": { "errors": {} }, "flash": { "toast": { "type": "success", "message": "Saved" } } }
```

Read it on the client with `usePage().flash` (or the `onFlash` visit callback).

**Where state is kept.** Flash data, errors, `clearHistory` and `preserveFragment` set during a request that ends in a redirect (or a `Location()`/fragment `409`) are saved in an `IInertiaStateStore` and loaded at the start of the next request. If that request redirects again, the state is carried on (so `Back()` to a URL that itself redirects still shows the flash); otherwise its response consumes it. The state is also kept when the response is the version-mismatch `409`.

- **Default: an encrypted cookie** (`.Inertia.State`). It is protected with ASP.NET Core Data Protection, HttpOnly, SameSite=Lax, and works without sessions and under Native AOT. Tampered or undecryptable cookies are ignored and deleted. It logs a warning above about 4 KB: browsers drop larger cookies.
- **Session store (opt in):** `o.State.UseSession()`, with `services.AddSession()` and `app.UseSession()` before `app.UseInertia()`.
- **TempData store (MVC package):** `AddInertiaMvc(o => o.UseTempDataStateStore())` keeps the state in MVC TempData, so it follows your TempData provider: the default cookie provider, or `AddSessionStateTempDataProvider()` with `AddSession()` and `app.UseSession()` before `app.UseInertia()`. It works for Minimal API endpoints too (the store saves TempData itself). Not Native AOT compatible.
- **Your own store:** implement `IInertiaStateStore` (`LoadAsync`, `SaveAsync`, `ClearAsync`, all moving opaque bytes) and register it before `AddInertia`.

**Which one?** Use the default cookie unless you have a reason not to (stateless, AOT safe, ~4 KB limit). Choose the session store when flash data can be large or must not reach the browser, and you already run sessions. Choose the TempData store when the app already standardizes on MVC TempData (for example one provider shared with `TempData["..."]` in views), or you want to switch between cookie and session TempData by changing only the provider.

In a multi-instance deployment, configure Data Protection with a shared key ring, or the cookie written by one instance cannot be read by another.

---

## Redirects, Location and Back

```csharp
return Results.Redirect("/users");                 // standard redirect: works with Inertia
return Back();                                     // to the Referer, else "~/" (the app root, PathBase included)
return Back("/users");                             // to the Referer, else "/users"
return Location("https://billing.example.com");    // leave the SPA
```

What `UseInertia()` does for Inertia requests:

| Situation | Result |
|---|---|
| 302 for PUT, PATCH or DELETE | changed to **303 See Other**, so the browser follows with a GET |
| Redirect whose `Location` contains `#`, and the request is not a prefetch | `409` with `X-Inertia-Redirect: <url>` (the client performs a full visit that keeps the fragment) |
| `Location(url)` | `409` with `X-Inertia-Location: <url>` for Inertia requests, a plain `302` otherwise |
| An empty `200` response | redirect back (302, or 303 for PUT/PATCH/DELETE), falling back to the app root (`PathBase/`) |
| `Back()` | `302` to the `Referer` when it is an http(s) URL with the request's own host and port, otherwise to the fallback (default `~/`, the app root including `PathBase`) |

The same-origin rule on `Referer` exists so `Back()` cannot become an open redirect. The scheme is not compared, so it keeps working behind a TLS-terminating proxy.

`Vary: X-Inertia` is added to every response that passes through the middleware.

---

## History encryption, clearing and fragments

```csharp
builder.Services.AddInertia(o => o.EncryptHistory = true);   // every page

context.Inertia().EncryptHistory();                          // this response
context.Inertia().EncryptHistory(false);                     // opt out of the global setting for this response
context.Inertia().ClearHistory();                            // clear the history state, typically before a redirect (e.g. logout)
context.Inertia().PreserveFragment();                        // keep the URL fragment across the next redirect
```

`encryptHistory` is written as `true` only when set and is not carried across redirects. `clearHistory` and `preserveFragment` go through the state store and apply to the next page that is rendered after the redirect(s), then they are consumed.

```csharp
app.MapPost("/logout", (HttpContext context) =>
{
    context.Inertia().ClearHistory();
    return Results.Redirect("/login");
});
```

---

## Asset versioning

The version tells the client when its assets are stale. The server resolves it as follows:

1. `InertiaOptions.VersionResolver(HttpContext)`, when it returns non-null;
2. else `InertiaOptions.Version`;
3. else a hash of the Vite manifest (the first 16 bytes of its SHA-256, in hex), computed once, and recomputed in Development when the file changes;
4. else `""`.

```csharp
o.Version = typeof(Program).Assembly.GetName().Version!.ToString();   // or a build id
o.VersionResolver = ctx => ctx.Request.Headers["X-Tenant"].ToString();   // per request
```

When an Inertia **GET** carries a different `X-Inertia-Version`, the server answers `409` with `X-Inertia-Location` (the absolute request URL) and `X-Inertia-Version`, and the client does a full page reload. The check runs **before your handler**, so no work is wasted rendering a page that is thrown away. Other methods never get this 409.

---

## Server-side rendering

SSR renders the first visit on a Node server using your client bundle, so crawlers and slow devices get HTML.

### 1. Build the SSR bundle

```tsx
// resources/js/ssr.tsx
import { createInertiaApp } from '@inertiajs/react'
import createServer from '@inertiajs/react/server'
import ReactDOMServer from 'react-dom/server'

createServer((page) =>
  createInertiaApp({
    page,
    render: ReactDOMServer.renderToString,
    resolve: (name) => {
      const pages = import.meta.glob('./Pages/**/*.tsx', { eager: true })
      return pages[`./Pages/${name}.tsx`]
    },
    setup: ({ App, props }) => <App {...props} />,
  }),
)
```

The client entry then hydrates when the server rendered:

```tsx
// resources/js/app.tsx: replace the setup function
import { createRoot, hydrateRoot } from 'react-dom/client'

setup({ el, App, props }) {
  if (el.dataset.serverRendered === 'true') hydrateRoot(el, <App {...props} />)
  else createRoot(el).render(<App {...props} />)
},
```

<details><summary>Vue and Svelte SSR entries</summary>

```ts
// Vue: resources/js/ssr.ts
import { createInertiaApp } from '@inertiajs/vue3'
import createServer from '@inertiajs/vue3/server'
import { createSSRApp, h } from 'vue'
import { renderToString } from 'vue/server-renderer'

createServer((page) =>
  createInertiaApp({
    page,
    render: renderToString,
    resolve: (name) => {
      const pages = import.meta.glob('./Pages/**/*.vue', { eager: true })
      return pages[`./Pages/${name}.vue`]
    },
    setup({ App, props, plugin }) {
      return createSSRApp({ render: () => h(App, props) }).use(plugin)
    },
  }),
)
// client entry: use createSSRApp(...).use(plugin).mount(el) instead of createApp
```

```ts
// Svelte: resources/js/ssr.ts
import { createInertiaApp } from '@inertiajs/svelte'
import createServer from '@inertiajs/svelte/server'
import { render } from 'svelte/server'

createServer((page) =>
  createInertiaApp({
    page,
    resolve: (name) => {
      const pages = import.meta.glob('./Pages/**/*.svelte', { eager: true })
      return pages[`./Pages/${name}.svelte`]
    },
    setup({ App, props }) {
      return render(App, { props })
    },
  }),
)
// client entry: if (el.dataset.serverRendered === 'true') hydrate(App, { target: el, props }) else mount(App, { target: el, props })
```

</details>

Build both bundles. The SSR build writes a Node script; run it with Node:

```json
{ "scripts": { "build": "vite build && vite build --ssr resources/js/ssr.tsx --outDir ssr" } }
```

```sh
node ssr/ssr.js        # listens on http://127.0.0.1:13714
```

The SSR build leaves `node_modules` imports external, so run the server where `node_modules` is installed (a Docker image with `npm ci --omit=dev` is the usual way). Inertia.Net does not start or supervise this process. Run it with a process manager (systemd, pm2) or as a sidecar container (`docker compose` service) and point `o.Ssr.Url` at it.

### 2. Enable it

```csharp
builder.Services.AddInertia(o =>
{
    o.Ssr.Enabled = true;
    o.Ssr.Url = "http://127.0.0.1:13714";           // default
    o.Ssr.Timeout = TimeSpan.FromSeconds(5);         // default
    o.Ssr.BundlePath = "ssr/ssr.js";                 // skip SSR while this file does not exist (relative to the content root)
    o.Ssr.ExcludePaths.Add("/admin/*");              // never SSR these (exact match, or a trailing * for a prefix), Request.Path without PathBase
    o.Ssr.ThrowOnError = false;                      // default: log and fall back to client-side rendering
});
```

- The server posts the page JSON to `{Url}/render` and reads `{ "head": [...], "body": "..." }`. `@inertiaHead` outputs the head tags joined with `\n`, and `@inertia` outputs the body verbatim.
- On a structured `500` from the SSR server, a timeout, a connection error or an empty response, Inertia.Net logs a warning (with the server's `hint` and source location when it sends them) and **falls back to client-side rendering**. With `ThrowOnError = true` it throws `InertiaSsrException` (`Type` is the server's error type or `connection`, `timeout`, `http`, `response`; `Hint` carries the fix suggestion).
- SSR runs only for first visits (the HTML response), never for Inertia XHR requests.

### 3. Development

With the Vite hot file present (Development only), or `o.Vite.DevServerUrl` set, Inertia.Net posts to `{viteDevUrl}/__inertia_ssr` instead. That endpoint is served by the `@inertiajs/vite` plugin, so you need no built bundle and no Node server:

```ts
// vite.config.ts, add to plugins
import inertia from '@inertiajs/vite'
inertia({ ssr: { entry: 'resources/js/ssr.tsx' } })
```

`BundlePath` is ignored while the dev server runs.

### 4. Health check

```csharp
builder.Services.AddHealthChecks().AddInertiaSsr();   // name "inertia-ssr"; optional failureStatus, tags, timeout
app.MapHealthChecks("/health");
```

The check reports healthy while `GET {Ssr.Url}/health` answers 2xx.

---

## Vite integration

`ViteAssets` (a singleton, also usable directly: `vite.RenderTags(httpContext, "resources/js/app.tsx")`) renders the tags for the `@vite(...)` token and the `<vite />` tag helper.

**Production.** It reads `wwwroot/build/.vite/manifest.json` (falling back to `wwwroot/build/manifest.json` for Vite 4 and older) once and pre-renders the tags to UTF-8. For each entry it emits, in order:

1. `<link rel="stylesheet">` for the entry's CSS and the CSS of its static imports (recursively, deduplicated);
2. `<script type="module" src="...">` for the entry file;
3. `<link rel="modulepreload">` for each static import.

URLs are `{PathBase}/{BuildDirectory}/{file}` (so apps under a path base work), or `{AssetBaseUrl}/{file}`. An entry missing from the manifest throws with the list of available entries. A missing manifest (and no dev server) throws with the paths it looked in.

**Development.** While the dev server runs, the tags are `@vite/client` and the entry from the dev server origin, plus the React Refresh preamble for `@viteReactRefresh`. The dev server counts as running when:
- `o.Vite.DevServerUrl` is set (always, in any environment), or
- the environment is Development and the hot file (`wwwroot/hot` by default) exists and contains the origin.

```csharp
builder.Services.AddInertia(o =>
{
    o.Vite.PublicDirectory = "wwwroot";                        // default
    o.Vite.BuildDirectory = "build";                           // default; Vite's outDir is PublicDirectory/BuildDirectory
    o.Vite.ManifestPath = null;                                // default wwwroot/build/.vite/manifest.json; relative to the content root
    o.Vite.HotFilePath = null;                                 // default wwwroot/hot
    o.Vite.DevServerUrl = builder.Environment.IsDevelopment() ? "http://localhost:5173" : null;   // alternative to the hot file
    o.Vite.AssetBaseUrl = "https://cdn.example.com/build";     // serve built assets from a CDN
});
```

Only plain http(s) origins without quotes or markup are accepted from the hot file or `DevServerUrl`, because the origin is echoed into a script.

With `AssetBaseUrl` set, Vite's own `base` must match, so lazily imported chunks resolve against the CDN too: `base: 'https://cdn.example.com/build/'`.

---

## CSRF and antiforgery

The Inertia client (axios) reads the `XSRF-TOKEN` cookie and sends its value as the `X-XSRF-TOKEN` header. Inertia.Net writes that cookie and you validate it with ASP.NET Core antiforgery:

```csharp
builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");

app.UseInertia();
app.UseAuthentication();
app.UseInertiaAntiforgeryCookie();      // after authentication, so the token is bound to the signed-in user
app.UseAntiforgery();                   // validates form bodies; see below for JSON bodies
```

- `UseInertiaAntiforgeryCookie(cookieName = "XSRF-TOKEN")` writes the request token to a script-readable cookie (`HttpOnly` off, SameSite=Lax, Secure on HTTPS, Path=/) on page loads: GET/HEAD requests that are Inertia requests or accept `text/html`. API calls and POSTs do not get it.
- `app.UseAntiforgery()` validates requests that have a form body (and endpoints with antiforgery metadata). For JSON endpoints, call `await antiforgery.ValidateRequestAsync(context)` (`IAntiforgery`) from a middleware or endpoint filter.
- MVC: add `[AutoValidateAntiforgeryToken]` as a global filter.

---

## Native AOT

`Inertia.Net` and `Inertia.Net.FastEndpoints` are annotated `IsAotCompatible` and a real app is published with Native AOT in CI (`tests/Inertia.Net.AotSmoke`, with zero trim and AOT warnings allowed). The library never reflects over your props: values are written with `JsonTypeInfo` from your `JsonSerializerContext`.

**1. Register an app `JsonSerializerContext` for every prop type**, and chain it into the HTTP JSON options:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

[JsonSerializable(typeof(UsersPage))]               // typed page props
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]           // closed collection types you return
[JsonSerializable(typeof(UserDto[]))]
[JsonSerializable(typeof(Stats))]                   // the T of Defer(() => new Stats(...)) and friends
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

Register the type that each loader returns and each plain value you place in `InertiaProps`, plus flash values that are not plain strings or numbers (flash is serialized through the same options). The library's own context already covers `string`, `bool`, `int`, `long`, `double`, `decimal`, `float`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `JsonElement`, `string[]`, `int[]`, `List<string>`, `Dictionary<string, string>`, `Dictionary<string, string[]>`, `InertiaProps` and `Dictionary<string, object>`, so those need no registration. Members of type `InertiaProp` are accepted by the source generator without any attribute.

**2. Validation model types must be public.** The .NET 10 `AddValidation()` source generator skips internal types, and validation then silently does nothing. Only one `AddValidation()` call site per project is allowed.

**3. Call `services.AddProblemDetails()`.** The non-Inertia `400` validation problem needs the `ProblemDetails` JSON metadata, or it cannot be serialized without reflection.

**4. Anonymous types do not work as props or flash values** under AOT. Use `InertiaProps`, records or classes registered in the context.

**5. Content files.** `app.html` and the Vite manifest are read from disk at run time. Copy them to the publish directory, and set the content root to the binary's directory when it may run from elsewhere:

```csharp
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
```

```xml
<!-- csproj -->
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>  <!-- catch missing registrations early -->
</PropertyGroup>
<ItemGroup>
  <Content Include="app.html" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

**6. MVC is not AOT compatible** (neither is MVC itself), so `Inertia.Net.Mvc` is not either. Use Minimal APIs or FastEndpoints for AOT.

**7. FastEndpoints** needs its source generator discovery instead of reflection scanning:

```csharp
builder.Services.AddFastEndpoints(DiscoveredTypes.All);
```

(`DiscoveredTypes` is generated by the `FastEndpoints.Generator` package, in the namespace named after the assembly. See [samples/FastEndpoints.Svelte](samples/FastEndpoints.Svelte).)

The reference app is [tests/Inertia.Net.AotSmoke](tests/Inertia.Net.AotSmoke/Program.cs). Run it end to end with `bash eng/aot-smoke.sh` (Linux, needs clang and zlib).

---

## Testing

`Inertia.Net.Testing` has Laravel-style fluent assertions over Inertia responses and `HttpClient` helpers. It parses both JSON responses (`X-Inertia: true`) and the first-visit HTML `<script data-page>`, and decodes `$bigint` values. Failures throw `InertiaAssertionException` with a readable message, so it works with any test framework.

```csharp
using Inertia.Net.Testing;
using Microsoft.AspNetCore.Mvc.Testing;

public class UsersTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Keeps cookies (flash, errors) and does not follow redirects, so a test can assert the 303 itself.
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Index_lists_users()
    {
        using var response = await _client.InertiaGetAsync("/users");          // sends X-Inertia, X-Requested-With

        await response.AssertInertiaAsync(page => page
            .Component("Users/Index")
            .Url("/users")
            .Has("users", 3, first => first.Where("name", "Ann").WhereType("id", "integer").Etc())
            .Where("filters.search", "")
            .Missing("secret")
            .HasFlash("toast", "Welcome"));
    }
}
```

`InertiaGetAsync(url, version?, configure?)` and `InertiaRequestAsync(method, url, content?, version?, configure?)` send an Inertia request. `GetInertiaPageAsync()` returns the parsed `InertiaPage` (also usable for full-HTML first visits). `AssertInertiaAsync(assert)` parses, runs the assertions and returns the page.

### Assertions

On the page: `Component`, `Url`, `Version`, `HasFlash(key)`, `HasFlash(key, value)`, `MissingFlash(key)`, `HasDeferred(path)`, `HasDeferred(group, path)`.

On props and nested scopes (dot paths, array indexes allowed): `Has(key)`, `Has(key, count)`, `Has(key, scope)`, `Has(key, count, firstItemScope)`, `HasAll`, `HasAny`, `Missing`, `MissingAll`, `Where(key, value)`, `Where(key, predicate)`, `WhereNot`, `WhereNull`, `WhereNotNull`, `WhereType(key, "string|null")`, `WhereAllType`, `WhereContains(key, ...)`, `Count`, `CountBetween`, `Each(scope)`, `First(scope)`, `Dump()`.

**Strict interaction tracking.** Like Laravel, a nested scope fails when it contains properties you did not assert, unless you end it with `Etc()`. The root scope is not checked automatically: call `.Interacted()` at the end to enforce that every top-level prop was asserted.

### Partial reloads and deferred props

`InertiaPage` replays the request with the right headers (`X-Inertia-Partial-Component`, the page's `url` and `version`):

```csharp
var page = await (await _client.InertiaGetAsync("/dashboard")).GetInertiaPageAsync();

new AssertableInertia(page).HasDeferred("sidebar", "stats").Missing("stats");

var loaded = await page.LoadDeferredPropsAsync(_client, "sidebar");     // all deferred groups when none are named
new AssertableInertia(loaded).Has("stats");

var only = await page.ReloadOnlyAsync(_client, "users");                // also asserts that "users" is in the result
var except = await page.ReloadExceptAsync(_client, "users");            // also asserts that "users" is missing
```

Other `InertiaPage` members: `Props`, `Raw`, `Prop(path)`, `HasProp(path)`, `PropBigInteger(path)`, `DeferredProps`, `MergeProps`, `PrependProps`, `DeepMergeProps`, `MatchPropsOn`, `RescuedProps`, `SharedProps`, `ScrollProps`, `OnceProps`, `Flash`, `EncryptHistory`, `ClearHistory`, `PreserveFragment`, `PreserveBigIntegers`.

Flash and errors travel in a cookie across the redirect, so use a client that keeps cookies (the `WebApplicationFactory` default). Disable `AllowAutoRedirect` to assert the redirect itself, then follow it with a second `InertiaGetAsync` to read the flash or errors. Add `public partial class Program;` to your app when it uses top-level statements.

---

## Performance

Measured with BenchmarkDotNet on a laptop (i5-8265U, .NET 10.0.12). Each render benchmark runs the result against a reused `DefaultHttpContext` with a null body: header parsing, shared props, the prop walk, the pooled buffer and the copy to the response. Timings are noisy (about ±10%); allocation numbers are exact.

| Benchmark | Mean | Allocated |
|---|---:|---:|
| JSON, 20 mixed props | 13.7 µs | 2,744 B |
| JSON, typed POCO props | 9.5 µs | 2,464 B |
| Partial reload, 2 of 20 props | 6.1 µs | 1,576 B |
| Deferred group load (2 props) | 4.7 µs | 1,584 B |
| HTML + Vite tags, 20 props (template view) | 14.7 µs | 2,784 B |
| *Reference: `JsonSerializer.Serialize` of the same page as plain data* | 9.3 µs | 312 B |
| Middleware: endpoint only (baseline) | 40 ns | 0 B |
| Middleware: `UseInertia`, non-Inertia request | 193 ns | 0 B |
| Middleware: `UseInertia`, Inertia request (version check) | 345 ns | 0 B |
| Parse partial-reload headers | 460 ns | 272 B |

Full results and the allocation-tuning history are in [tests/Inertia.Net.Benchmarks/RESULTS.md](tests/Inertia.Net.Benchmarks/RESULTS.md). Re-run with `dotnet run -c Release -- --filter '*'` in `tests/Inertia.Net.Benchmarks`.

Design notes:

- **Single pass.** One recursive walk filters props, runs loaders, writes JSON to a `Utf8JsonWriter` and collects the metadata. There is no intermediate object tree.
- **Pooled buffers.** The page is written to a pooled `IBufferWriter<byte>`, and the path tracker is a pooled `char[]`. A path becomes a string only when metadata needs it. The buffer also means a failing loader never produces a half-written 200.
- **Version check before the handler.** A stale client costs one header comparison and a 409.
- **No per-request mutable singletons.** Per-request state lives in an `InertiaFeature` on `HttpContext.Features`, so concurrent requests cannot leak history or flash settings into one another.
- **Plain DTOs take the fast path.** A POCO or list is walked only when its type can hold `InertiaProp` values (`InertiaProp`, `object` or dictionary members/elements; cached per type). Everything else is one `Serialize` call.
- **Cached assets.** The Vite manifest is parsed once, the root template is parsed once into pre-encoded UTF-8 segments, and the production tags are pre-rendered per entry set and path base. The version is a precomputed string compared ordinally.
- **Sync loaders stay sync.** `Func<T>` loaders are not wrapped in closures.

Most per-request allocation in a real handler is the props you build, not the render.

---

## Configuration reference

### `AddInertia(o => ...)`: `InertiaOptions`

| Option | Default | Description |
|---|---|---|
| `RootView` | `"app.html"` | Root template, relative to the content root (a Razor view name with `UseRazorRootView`). |
| `RootElementId` | `"app"` | Id of the root element and the `data-page` script. |
| `Version` | `null` | Asset version. `null` = hash of the Vite manifest, or `""` without one. |
| `VersionResolver` | `null` | `Func<HttpContext, string?>` per-request version; wins over `Version` when non-null. |
| `EncryptHistory` | `false` | Encrypt browser history state of every page. |
| `WithAllErrors` | `false` | Send every validation message per field instead of the first. |
| `PreserveBigIntegers` | `false` | Emit integers beyond ±(2^53−1) as `{"$bigint":"..."}` and set `preserveBigIntegers`. |
| `ExposeSharedPropKeys` | `true` | List top-level shared keys in `sharedProps`. |
| `Share(key, value)` / `Share(key, Func<HttpContext, object?>)` | none | Shared props. |

### `o.Vite`: `ViteOptions`

| Option | Default | Description |
|---|---|---|
| `PublicDirectory` | `"wwwroot"` | Web root containing the build directory and hot file. |
| `BuildDirectory` | `"build"` | Vite output directory under `PublicDirectory`; also the URL prefix. |
| `ManifestPath` | `null` | Manifest path relative to the content root. `null` = `{PublicDirectory}/{BuildDirectory}/.vite/manifest.json`, then `manifest.json`. |
| `HotFilePath` | `null` | Hot file relative to the content root. `null` = `{PublicDirectory}/hot`. Read in Development only. |
| `DevServerUrl` | `null` | Dev server origin, e.g. `http://localhost:5173`. When set, dev tags always render and the hot file is ignored. |
| `AssetBaseUrl` | `null` | Replaces `{PathBase}/{BuildDirectory}` as the URL prefix of built assets (CDN). |

### `o.Ssr`: `SsrOptions`

| Option | Default | Description |
|---|---|---|
| `Enabled` | `false` | Render first visits on the SSR server. |
| `Url` | `"http://127.0.0.1:13714"` | SSR server base URL (`/render`, `/health`). |
| `Timeout` | `5 s` | Wait for the SSR server (also the health check timeout). |
| `ThrowOnError` | `false` | Throw `InertiaSsrException` instead of falling back to client rendering. |
| `BundlePath` | `null` | Skip SSR while this file is missing, unless the Vite dev server runs. |
| `ExcludePaths` | empty | Request paths never rendered on the server: exact, or a trailing `*` for a prefix. |

### `o.State`: `InertiaStateOptions`

| Option | Default | Description |
|---|---|---|
| `CookieName` | `".Inertia.State"` | Cookie name (cookie store) or session key (session store). |
| `UseSession()` | off | Use `ISession` instead of the encrypted cookie. |
| `AddInertiaMvc(o => o.UseTempDataStateStore())` | off | Use MVC TempData (cookie or session provider) instead of the encrypted cookie. Inertia.Net.Mvc package. |

### Middleware, endpoint and result API

| API | Description |
|---|---|
| `services.AddInertia(configure?)` | Registers services and options. |
| `app.UseInertia()` | The protocol middleware. Before the endpoints, after `UseSession()` when the session store is used. |
| `app.UseInertiaAntiforgeryCookie(cookieName = "XSRF-TOKEN")` | Writes the antiforgery request token to a script-readable cookie. |
| `endpoint.WithInertiaValidation()` | Minimal API: validation failures redirect back with errors. Endpoint or group. |
| `endpoint.WithInertiaPrecognition()` | Minimal API: `WithInertiaValidation()` plus Precognition. |
| `healthChecks.AddInertiaSsr(name = "inertia-ssr", failureStatus?, tags?, timeout?)` | SSR health check. |
| `mvc.AddInertiaMvc(o => o.UseRazorRootView("App"))` | MVC filter, optional Razor root view. |
| `config.UseInertia(configure?)` | FastEndpoints: validation redirect and Precognition (`app.UseFastEndpoints(c => ...)`). |
| `Render(component, props?)` | `InertiaResult`: `.WithStatusCode(int)`, `.WithRootView(string)`, `.WithViewData(key, value)`. |
| `Location(url)` | `InertiaLocationResult`. |
| `Back(fallback = "~/")` | `InertiaBackResult`: `.WithErrors(errors, bag = "default")`, `.WithFlash(key, value)`. |
| `httpContext.Inertia()` | `InertiaFeature`: `Share`, `Flash`, `WithErrors`, `ClearHistory`, `EncryptHistory(bool = true)`, `PreserveFragment`. |
| `Send.InertiaAsync(component, props?)`, `Send.InertiaLocationAsync(url)`, `Send.InertiaBackAsync(fallback = "~/")` | FastEndpoints `Send` extensions. |

Extension points: `IInertiaRootView`, `IInertiaStateStore`, `ViteAssets`, `InertiaRequest` (the parsed headers), `InertiaHeaders` (header name constants), `InertiaSsrException`.

---

## Migrating from other .NET adapters

The 1.0 API is close to the Laravel adapter's names. The table maps the typical InertiaCore-style API (kapi2289/InertiaCore and its forks). Check names against the version you use.

| Other adapters | Inertia.Net |
|---|---|
| `Inertia.Render("Users/Index", new { users })` | `Render("Users/Index", new InertiaProps { ["users"] = users })`, or a typed props object |
| `return Inertia.Render(...)` in a controller (`Task<IActionResult>`) | `return Render(...)` as `IResult` (MVC executes it) |
| `Inertia.Lazy(() => ...)` / `LazyProp` | `Optional(() => ...)` (never on first load) or `Prop(() => ...)` (lazy, included by default) |
| `Inertia.Defer(...)` / `Inertia.Defer(..., "group")` | `Defer(..., group: "group")` |
| `Inertia.Always(...)` | `Always(...)` |
| `Inertia.Merge(...)` / `DeepMerge` | `Merge(...)`, `DeepMerge(...)`, plus `.Prepend()`, `.MatchOn()` |
| (not supported) | `Once(...)`, `Scroll(...)`, `.Rescue()` |
| `Inertia.Share("key", value)` | `o.Share("key", value)`, `o.Share("key", ctx => ...)`, or `ctx.Inertia().Share(...)` |
| `Inertia.Location(url)` | `Location(url)` |
| `Inertia.Back()` | `Back()` / `Back(fallback)` |
| `Inertia.ClearHistory()`, `Inertia.EncryptHistory()` | `ctx.Inertia().ClearHistory()`, `ctx.Inertia().EncryptHistory()` (per request, no shared state) |
| `Inertia.Html(...)` / `Inertia.Head` in `_Layout.cshtml`, or `AddInertia(o => o.RootView = "~/Views/App.cshtml")` | `app.html` with `@inertia`, `@inertiaHead`, `@vite(...)`, or the Razor tag helpers `<inertia />`, `<inertia-head />`, `<vite />` |
| Flash via `TempData` or session | `ctx.Inertia().Flash(...)`, `Back().WithFlash(...)` (encrypted cookie by default) |
| `ModelState` errors in session | MVC: `AddInertiaMvc()`; Minimal API: `.WithInertiaValidation()`; FastEndpoints: `c.UseInertia()` |
| `Version` option or manifest hashing per request | `o.Version`, `o.VersionResolver`, or the cached manifest hash |
| SSR options (`UseSsr`, `SsrUrl`) | `o.Ssr.Enabled`, `o.Ssr.Url`, `o.Ssr.ExcludePaths`, `o.Ssr.ThrowOnError` |

Behavior differences to check when you migrate:

- v3 only: no v2 clients. Upgrade the client packages to v3. The first page is the `<script data-page="app">` element, not a `data-page` attribute on the root div.
- Props are resolved without reflection: anonymous types need reflection-enabled serialization, and AOT apps register a `JsonSerializerContext`.
- Props serialize with the HTTP JSON options (camelCase by default), not Newtonsoft.
- The version check runs before the handler.
- Flash, errors and history flags use an encrypted cookie unless you opt into the session store.

---

## Troubleshooting and FAQ

**`Inertia.Render(...)` does not compile / "'Inertia' is a namespace".**
Call `Render(...)` unqualified. The namespace `Inertia.Net` shadows the class name outside the `Inertia.Net` namespace. The global usings come from the package's `buildTransitive` props and need `<ImplicitUsings>enable</ImplicitUsings>`. Without implicit usings (or with `InertiaNetImplicitUsings` set to `false`) add `using static Inertia.Net.Inertia;` to the file. See the [callout](#installation). A class library that references `Inertia.Net` gets the same global usings.

**Generated code fails with "'Inertia' is a type" (e.g. FastEndpoints' source generator).**
Inside a namespace that starts with `Inertia.Net.`, the name `Inertia` binds to the `Inertia.Net.Inertia` class. Source generators that emit non-`global::` names starting with `Inertia` then break. Don't put your app's code under an `Inertia.Net.*` namespace.

**`Vite manifest not found. Looked in: ...`**
Run `npm run build`, or start the dev server so the hot file exists (in Development), or set `o.Vite.ManifestPath` / `o.Vite.BuildDirectory`. Check that Vite writes `.vite/manifest.json` (`build.manifest: true`, Vite 5 and later) to `wwwroot/build`, and that the build output is part of what you deploy.

**`Vite entry 'resources/js/app.tsx' was not found in the manifest`**
The `@vite("...")` argument must equal the manifest key, which is the path relative to the Vite root. The error lists the available entries.

**The page keeps reloading, or a request loops with `409`.**
A 409 means the asset version differs between the client and the server. A loop means it never converges: usually the version changes on every request (a `VersionResolver` that returns a new value each time, or a manifest regenerated by a file watcher), or different instances behind a load balancer serve different builds. Use a stable version string. In Development the manifest hash changes after each `vite build`, which is expected once.

**The dev server tags do not appear.**
The hot file is read only when `ASPNETCORE_ENVIRONMENT=Development`. Check that `wwwroot/hot` exists and contains `http://localhost:5173`, or set `o.Vite.DevServerUrl`. Remember `base: '/'` for the dev server in `vite.config` (the `command === 'build'` check in the example).

**`The Inertia root template '...app.html' was not found` or a missing `@inertia` token.**
`RootView` is relative to the **content root**. Copy `app.html` to the output and publish directories (`CopyToOutputDirectory`, `CopyToPublishDirectory`). The template must contain `@inertia`.

**Flash data or errors disappear, or a warning says the cookie is over 4 KB.**
The state cookie is limited to about 4 KB by browsers. Flash less data, or use `o.State.UseSession()`. If the app runs on several instances, share Data Protection keys. If the cookie is never sent back, check the cookie `Secure` flag on HTTP behind a proxy that does not forward `X-Forwarded-Proto`.

**Validation errors do not show up (Minimal API).**
Use `.WithInertiaValidation()` on the endpoint. Without it, the built-in `AddValidation()` filter returns a `400` problem details that the Inertia client cannot use. Make sure the model types are `public` (see [Native AOT](#native-aot)). The request must be an Inertia request and not a GET.

**Validation keys do not match my field names.**
Keys go through the JSON naming policy per segment (`Address.Street` becomes `address.street`). In MVC the policy is the one in the MVC `JsonOptions`. The page itself is written with the HTTP `JsonOptions`: keep both policies aligned.

**`InvalidOperationException: InertiaProp values can only be serialized by Inertia.Net`.**
An `InertiaProp` ended up somewhere Inertia.Net does not walk: a `Flash(...)` value, a dictionary with non-string keys or non-`object` values (`Dictionary<string, InertiaProp>`), or a type with a custom converter. Lists, `object` members and `IDictionary<string, object?>` are walked. Put props in `InertiaProps`, or declare the member as `InertiaProp` (or `object`) on the typed page object.

**Serialization fails under Native AOT: "JsonTypeInfo metadata ... was not provided".**
Add the type to your `JsonSerializerContext` and chain it with `ConfigureHttpJsonOptions` (see [Native AOT](#native-aot)). Setting `JsonSerializerIsReflectionEnabledByDefault=false` in regular builds finds these early.

**SSR does not run and there is no error.**
SSR falls back to client rendering on any failure: look for the warning `Inertia SSR failed (...)` in the logs, which includes the server's hint. Check `o.Ssr.Enabled`, `Url`, that the Node server is running (`GET /health`), that `BundlePath` points at the built file, and that the path is not in `ExcludePaths`. Set `ThrowOnError = true` in Development to surface the failure.

**The SSR output is not hydrated, or I see a hydration warning.**
Use the hydrating client entry shown in [Server-side rendering](#server-side-rendering) and build both bundles from the same sources. The SSR body is marked `data-server-rendered="true"`.

**Does it work behind a path base (`UsePathBase`)?**
Yes. The page `url` keeps the path base, the Vite URLs include it, and `Back()` works.

**Can I use Newtonsoft.Json?**
No. Props use System.Text.Json with the HTTP JSON options.

**Can I mix Razor Pages or views with Inertia pages?**
Yes. Inertia handles only the endpoints that return `Render(...)`. The middleware leaves other responses alone, apart from `Vary: X-Inertia`.

---

## Contributing and license

```sh
dotnet build Inertia.Net.slnx -c Release -warnaserror
dotnet test Inertia.Net.slnx -c Release
bash eng/aot-smoke.sh                    # Native AOT publish + protocol smoke test (Linux, clang + zlib)
```

- Warnings are errors; the code is nullable-annotated and the public API is documented.
- Every protocol rule should be backed by a test in `tests/Inertia.Net.Tests` or a conformance case in `tests/Inertia.Net.IntegrationTests` (which runs against Minimal API, MVC and FastEndpoints hosts). The protocol itself is in [docs/protocol.md](docs/protocol.md), including the deliberate deviations from inertia-laravel.
- Changes are listed in [CHANGELOG.md](CHANGELOG.md). Releases are cut by pushing a `v*` tag: the release workflow builds, tests, packs and publishes to NuGet.

Licensed under the [MIT license](LICENSE).
