# Inertia.Net

Build React, Vue or Svelte apps with ASP.NET Core, using your server's routes and controllers instead of a separate frontend API.

Inertia.Net **1.5.2** is a .NET 10 server adapter for the **Inertia.js v3** protocol. It supports Minimal APIs, MVC and FastEndpoints. Minimal API apps support Native AOT; FastEndpoints apps also need its source generator. MVC itself does not support Native AOT.

## Contents

- [How it works](#how-it-works)
- [Start a new app](#start-a-new-app)
- [Sample apps](#sample-apps)

1. [Overview and feature matrix](#overview-and-feature-matrix)
2. [Installation](#installation)
3. [Quick start](#quick-start): [Minimal API](#minimal-api), [MVC](#mvc), [FastEndpoints](#fastendpoints)
4. [Client setup](#client-setup): [Vite config](#vite-config), [React](#react), [Vue](#vue), [Svelte](#svelte)
5. [The root template](#the-root-template)
6. [Props reference](#props-reference), [parallel props](#parallel-props) and [cached props](#cached-props)
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
18. [Server-side rendering](#server-side-rendering): [failures](#when-the-server-fails), [several servers](#several-ssr-servers), [observability](#5-observability), [response cache](#6-response-cache)
19. [Vite integration](#vite-integration)
20. [CSRF and antiforgery](#csrf-and-antiforgery)
21. [Deploying behind a proxy](#deploying-behind-a-proxy)
22. [Native AOT](#native-aot)
23. [TypeScript types](#typescript-types), [DevTools](#devtools) and [development checks](#development-checks)
24. [Testing](#testing)
25. [Performance](#performance)
26. [Configuration reference](#configuration-reference)
27. [Migrating from other .NET adapters](#migrating-from-other-net-adapters)
28. [Troubleshooting and FAQ](#troubleshooting-and-faq)
29. [Contributing and license](#contributing-and-license)

---

## How it works

Your .NET endpoint chooses a frontend page and supplies its data as **props**. Inertia connects that endpoint to your React, Vue or Svelte component.

1. The first visit returns an HTML document with the page's data.
2. Subsequent Inertia links and form submissions request page data in the background.
3. The client updates the page without a full browser reload. Your .NET app still owns routing, authentication and validation.

For example, this endpoint renders a page named `Home` with a `greeting` prop:

```csharp
app.MapGet("/", () => Render("Home", new InertiaProps
{
    ["greeting"] = "Hello from .NET",
}));
```

The matching React page receives it as a normal component prop:

```tsx
// ClientApp/pages/Home.tsx (in the starter template)
export default function Home({ greeting }: { greeting: string }) {
  return <h1>{greeting}</h1>
}
```

The page name must match your client-side page resolver. The starter templates configure that resolver for you.

## Start a new app

You need the .NET 10 SDK and Node.js with npm. Use an Inertia v3 client; v2 clients are not supported.

Create an app with the starter template:

```sh
dotnet new install Inertia.Net.Templates::1.5.2
dotnet new inertia -n MyApp --framework react
cd MyApp
npm install
```

Choose `--framework vue` or `--framework svelte` for another frontend. Each template uses Minimal APIs and includes working pages, a Todos form, validation, flash messages, CSRF protection and Vite configuration.

Start Vite in one terminal, from `MyApp`:

```sh
npm run dev
```

Start ASP.NET Core in another terminal, also from `MyApp`:

```sh
dotnet watch
```

Open [http://localhost:5180](http://localhost:5180). Browse through ASP.NET Core; Vite supplies the frontend assets and hot reload.

For a production build, run `npm run build`, then `dotnet publish -c Release`. Deploy the built frontend assets with the published app. SSR is optional and requires a Node server at runtime; ordinary client rendering does not.

## Sample apps

[Inertia.Net-Samples](https://github.com/adzamkomladev/Inertia.Net-Samples) has five .NET 10 apps built with Inertia.Net, from basic page rendering to a cache shared across replicas. The three main ones run as live demos:

| App | What it is | Frontend | Database | Source | Live demo |
|---|---|---|---|---|---|
| Inkwell | Multi-user publishing platform with feeds, search, analytics and live notifications | React | SQLite | [samples/Inkwell](https://github.com/adzamkomladev/Inertia.Net-Samples/tree/main/samples/Inkwell) | [inkwell.yebi.africa](https://inkwell.yebi.africa) |
| TechLandier | Electronics storefront with BNPL, warranties, insurance, repairs and rider deliveries | Vue | PostgreSQL | [samples/TechLandier](https://github.com/adzamkomladev/Inertia.Net-Samples/tree/main/samples/TechLandier) | [techlandier.yebi.africa](https://techlandier.yebi.africa) |
| CarParker | Parking-spot rentals with sessions, refunds, spot cover and a guard console | Svelte | MongoDB | [samples/CarParker](https://github.com/adzamkomladev/Inertia.Net-Samples/tree/main/samples/CarParker) | [carparker.yebi.africa](https://carparker.yebi.africa) |

The other two, CarParkerReplicas and TechLandierDistributed, run CarParker and TechLandier across replicas and run locally only. Each sample's README lists its demo logins and how to run it.

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
- [x] `Vary: X-Inertia`, 302 to 303 for PUT/PATCH/DELETE, fragment redirects (`X-Inertia-Redirect`), `Location()`, `Back()`, and
  redirects to another origin turned into `409` + `X-Inertia-Location`
- [x] Partial reloads (`only`, `except`, nested dot paths), `Always`, `Optional`, lazy loaders, opt-in parallel loaders
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
- [x] Server-side rendering (production Node servers, optionally started and supervised by the app, Vite dev server, per-response modes, circuit breakers, render slots, metrics, health check)
- [x] Vite manifest and dev-server tags, CDN asset base URL, chunk prefetching, CSP nonces
- [x] Inertia DevTools protocol (discovery, correlation headers, entries, read API)

No v2 protocol compatibility. MVC TempData storage is available through `AddInertiaMvc(o => o.UseTempDataStateStore())`.

---

## Installation

| Package | Install when |
|---|---|
| `Inertia.Net` | always. The core adapter, Minimal API support, Vite, SSR, state store, antiforgery cookie |
| `Inertia.Net.Mvc` | you use MVC controllers: `ModelState` to errors filter, Razor root view and tag helpers |
| `Inertia.Net.FastEndpoints` | you use FastEndpoints: `Send` extensions, validation pre-processor, Precognition |
| `Inertia.Net.Testing` | in your test project: fluent assertions over Inertia responses |
| `Inertia.Net.Templates` | `dotnet new install Inertia.Net.Templates`: `dotnet new inertia` starts an app (React, Vue or Svelte) |

```sh
dotnet add package Inertia.Net --version 1.5.2
dotnet add package Inertia.Net.Mvc --version 1.5.2 # MVC only
dotnet add package Inertia.Net.FastEndpoints --version 1.5.2 # FastEndpoints only
dotnet add package Inertia.Net.Testing --version 1.5.2 # test projects
```

Requires .NET 10 and an ASP.NET Core app (`Microsoft.NET.Sdk.Web`), plus Node.js and npm to build the frontend. Use version `1.5.2` for all Inertia.Net packages. Inertia.Net 1.5.2 uses the Inertia.js v3 protocol; these are separate version numbers.

> **Callout: `Render(...)`, `Defer(...)`, `Back()` without a prefix.**
> The namespace is `Inertia.Net` and the static factory class is `Inertia.Net.Inertia`. C# resolves a leading `Inertia` to the *namespace*, so `Inertia.Render(...)` does **not** compile in your app (outside the `Inertia.Net` namespace).
> The package's `buildTransitive` props file therefore adds `global using Inertia.Net;` and `global using static Inertia.Net.Inertia;` to your project when `<ImplicitUsings>enable</ImplicitUsings>` is set (the default in new projects). Write `Render(...)`, `Defer(...)`, `Once(...)`, `Back()`, `Location(...)` unqualified.
> To opt out set `<InertiaNetImplicitUsings>false</InertiaNetImplicitUsings>` and add `using static Inertia.Net.Inertia;` yourself, or call `Inertia.Net.Inertia.Render(...)`.
> `HttpContext.Inertia()` (per-request flash, errors, history flags) is an extension method and works as written.

---

## Quick start

Already using the starter template? Its services, middleware, root template and frontend are configured for you. The steps below are for adding Inertia to an existing app or building one manually. Choose one backend and one frontend.

For a new project without the starter template, run `dotnet new web -n MyApp`, then `cd MyApp`, install the packages above, and run `npm init -y` before the client setup.

Every backend needs the same four things: `AddInertia()`, `UseInertia()`, a root template, and a frontend (see [Client setup](#client-setup)).
Add CSRF protection from the start: `AddAntiforgery(...)` and `UseInertiaAntiforgery()`, as below (see [CSRF and antiforgery](#csrf-and-antiforgery)).

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
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Demo");
});

var app = builder.Build();

app.UseStaticFiles();                                // serves wwwroot/build (Vite output)
app.UseInertia();                                    // before the endpoints
app.UseInertiaAntiforgery();                         // XSRF-TOKEN cookie; 419 for mutations without a valid token

app.MapGet("/", () => Render("Home", new InertiaProps
{
    ["greeting"] = "Hello from .NET",
}));

app.Run();
```

This first page needs no database. Add a `Home` component using the client example for your framework below. The later CRUD examples use application-specific services such as `AppDb`; replace those with your own data layer. Form endpoints also need the validation setup in [Validation and error bags](#validation-and-error-bags).

### MVC

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInertia();
builder.Services.AddControllersWithViews().AddInertiaMvc();   // add .UseRazorRootView() options here if you want Razor
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();
app.UseInertiaAntiforgery();
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
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();
app.UseInertiaAntiforgery();
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

Run the npm commands from the ASP.NET Core project directory (the directory containing your `.csproj`). Install only the row for your chosen framework, followed by the shared development dependencies.

Inertia.Net speaks the v3 protocol: the initial page is the `<script data-page="app" type="application/json">` element, which the v3 clients read directly. Install the v3 client packages for your framework:

```sh
npm install react react-dom @vitejs/plugin-react @inertiajs/react@^3          # React
npm install vue @vitejs/plugin-vue @inertiajs/vue3@^3                          # Vue
npm install svelte @sveltejs/vite-plugin-svelte @inertiajs/svelte@^3           # Svelte
npm install -D vite typescript
```

The starter templates use `ClientApp/pages`; the manual setup below uses `resources/js/Pages`. Keep the resolver, file paths and Vite entry consistent with the layout you choose.

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

Add these fields to your `package.json`, keeping the dependencies installed above:

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

For the `GET /` endpoint above, create this page first:

```tsx
// resources/js/Pages/Home.tsx
export default function Home({ greeting }: { greeting: string }) {
  return <h1>{greeting}</h1>
}
```

The following `Users/Index` page illustrates a list supplied by your own `/users` endpoint:

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

Use `@vite("resources/js/app.ts")` in the template and `input: 'resources/js/app.ts'` in the Vite config. Replace the React plugin with `import vue from '@vitejs/plugin-vue'` and `plugins: [vue(), hotFile()]`. Remove `@viteReactRefresh`.

For the `GET /` endpoint above, create this page first:

```vue
<!-- resources/js/Pages/Home.vue -->
<script setup lang="ts">
defineProps<{ greeting: string }>()
</script>

<template><h1>{{ greeting }}</h1></template>
```

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

Use `@vite("resources/js/app.ts")` in the root template and `input: 'resources/js/app.ts'` in the Vite config. Replace the React plugin with `import { svelte } from '@sveltejs/vite-plugin-svelte'` and `plugins: [svelte(), hotFile()]`. Remove `@viteReactRefresh`.

For the `GET /` endpoint above, create this page first:

```svelte
<!-- resources/js/Pages/Home.svelte -->
<script lang="ts">
  let { greeting }: { greeting: string } = $props()
</script>

<h1>{greeting}</h1>
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

Open the ASP.NET Core URL printed by `dotnet watch`, rather than the Vite URL. The first visit to `/` should display "Hello from .NET". If you started with `dotnet new web`, ensure the launch profile sets `ASPNETCORE_ENVIRONMENT=Development` so the adapter reads the Vite hot file.

For production, run `npm run build`, then `dotnet publish -c Release`. Deploy `wwwroot/build` and `app.html` with the published app. Node.js is needed to build the assets; it is only needed at runtime if you enable SSR. Samples for each framework live in [samples/](samples/).

---

## The root template

The default root view parses an HTML file on its first render (and re-reads it on change in Development) into literal chunks plus tokens. It throws on that render when `@inertia` is missing. The path is `InertiaOptions.RootView` (default `app.html`) relative to the content root.

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

Fluent methods on every prop (they mutate and return the same instance): `.Once(key?, until?)`, `.As(key)`, `.Until(ttl)`, `.Fresh()`, `.Append(path?, matchOn?)`, `.Prepend(path?, matchOn?)`, `.DeepMerge()`, `.MatchOn(params keys)`, `.Defer(group)`, `.WithGroup(group)`, `.Rescue()`, `.Parallel()`, `.Cache(key, duration)`, `.Cache(key, duration, staleFor?, tags?)`.

**Loaders run sequentially** by default, in prop order, and only when their prop is included in the response. That makes a scoped EF Core `DbContext` safe to use in several loaders. Mark independent async loaders with [`.Parallel()`](#parallel-props) to start them together. A loader that returns another prop (for example `Prop(() => Defer(...))`) is unwrapped once.

The page is buffered before it is sent, so a loader that throws never produces a half-written 200.

### Parallel props

Props are resolved one after another by default. Mark independent async loaders with `.Parallel()` to start them together:

```csharp
return Render("Dashboard", new InertiaProps
{
    ["stats"] = Prop(ct => statsService.GetAsync(ct)).Parallel(),
    ["activity"] = Defer(ct => activityService.RecentAsync(ct)).Parallel(),
    ["user"] = currentUser, // values and sync loaders resolve in turn
});
```

- Only `Task`/`ValueTask` loaders can be parallel; `Parallel()` throws for values and synchronous loaders.
- At most `InertiaOptions.Props.MaxConcurrency` loaders run at once per render (default 4): `builder.Services.AddInertia(o => o.Props.MaxConcurrency = 8);`
- Top-level props and props inside literal objects (`InertiaProps` and other string-keyed dictionaries) start early. Elsewhere they resolve in turn.
- Loaders of props that aren't part of the response (partial reloads, deferred or optional props on the first visit) never start.
- Pages without parallel props skip the work of finding them. A component remembers whether its renders have parallel props. If it was first rendered without them and later gets some, that one render resolves them in turn, and every later render starts them early.
- If a loader fails, the others are cancelled and awaited, and the render fails with the first failure in prop order. `Rescue()`d props fail alone, as usual.

**DbContext and HttpContext:** a parallel loader must not use the request's `DbContext` or other non-thread-safe scoped services, and must not touch `HttpContext`. EF Core throws "A second operation was started on this context instance…". Give each loader its own context through `IDbContextFactory<T>` or its own scope:

```csharp
["orders"] = Prop(async ct =>
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);
    return await db.Orders.CountAsync(ct);
}).Parallel(),
```

### Cached props

`.Cache(key, duration)` keeps a prop's value as JSON in the cache (by default the app's `IMemoryCache`). While the entry lives, the
loader does not run and the stored JSON is written as it is, so a hit costs neither the query nor the serialization:

```csharp
return Render("Dashboard", new InertiaProps
{
    ["stats"] = Prop(ct => stats.ComputeAsync(ct)).Cache("dashboard-stats", TimeSpan.FromMinutes(5)),
    ["orders"] = Defer(ct => orders.RecentAsync(userId, ct)).Cache($"orders:{userId}", TimeSpan.FromMinutes(1), tags: [$"user:{userId}"]),
    ["catalog"] = Optional(() => catalog.All()).Cache("catalog", TimeSpan.FromHours(1), staleFor: TimeSpan.FromHours(23)),
});
```

- **The key is the value's identity.** Every request that uses a key gets the same value: put the user, tenant or anything else the
  value depends on in the key. Keys live in their own space in the cache, so they cannot collide with the app's own entries.
- **Only loading changes.** Optional, deferred, merged, once and scroll props keep their behavior; their metadata is written from the
  cache too. A parallel cached prop does not start its loader on a hit. A rescued failure is not cached.
- **Plain values only.** The value must not contain `InertiaProp` values (a loader that returns a prop cannot be cached).
- **Per instance.** With the default store each app instance has its own memory cache. Concurrent misses share one fill per key and
  app instance; if it fails or is cancelled, a waiting request retries with its own loader. Entries carry a `Size` (their byte length),
  so an `IMemoryCache` with a `SizeLimit` works.

**Removing and replacing values.** Inject `IInertiaCache` where the data changes:

```csharp
app.MapPost("/orders", async (OrderInput input, IInertiaCache cache, CancellationToken ct) =>
{
    var order = await orders.CreateAsync(input, ct);
    await cache.RemoveByTagAsync($"user:{order.UserId}", ct);    // every prop tagged with the user
    await cache.RemovePropAsync("dashboard-stats", ct);           // one key, as given to Cache()
    await cache.SetPropAsync("catalog", await catalog.AllAsync(ct), TimeSpan.FromHours(1), cancellationToken: ct); // replace it now
    return Back();
});
```

`ClearPropsAsync()` removes every cached prop and `ClearSsrAsync()` every [cached render](#6-response-cache). `SetPropAsync` serializes
the value as a prop of its type would be (a scroll value's paging metadata comes from `IProvidesScrollMetadata`), and takes the same
`staleFor` and `tags`. With the default store a removal frees the entries at once.

**Stale-while-revalidate.** `staleFor` keeps serving a value once its `duration` has passed. The first request that reads a stale
value gets it at once, and the loader runs after that response has been sent, to store a fresh value; requests in the meantime get the
stale value too. Past `duration + staleFor` the loader runs during the request again, as without `staleFor`.

- The refresh runs in the response's completion callbacks, which run before the request's services are disposed: a loader can keep
  using the request's `DbContext` and other scoped services. It must not touch the `HttpContext` (the response is already sent).
  On HTTP/1.1 the connection takes its next request once the refresh has finished.
- One refresh runs per key and app instance at a time, and the stale props of one response refresh one after another. A refresh
  that finds the value fresh again (another refresh, or another app instance sharing the store, stored one since) skips its loader.
- A refresh that fails is logged as a warning and keeps the stale value; the next stale read tries again.

**Another store.** Props and [SSR renders](#6-response-cache) are kept in an `IInertiaCacheStore`. Register your own as a singleton
(before or after `AddInertia()`) to keep them elsewhere, e.g. in a cache every app instance shares:

```csharp
builder.Services.AddSingleton<IInertiaCacheStore, MyRedisInertiaCacheStore>();
```

The store gets and sets byte arrays by `InertiaCacheKey` (a kind and a key; `ToString()` gives `inertia:prop:{key}` or
`inertia:ssr:{hash}` for stores keyed by strings), removes by key and by tag. The values carry their own fresh and expiry times, so a
store only keeps each one for `InertiaCacheEntryOptions.Expiration`. Every entry also carries `InertiaCacheTags.Props` or
`InertiaCacheTags.Ssr`, which the clear methods remove. While a page renders, a store that throws is
logged (at most once every 30 seconds) and counts as a miss; `IInertiaCache` lets the exception through. A value in a format Inertia.Net
does not know reads as a miss. Prop JSON outlives a deploy in a shared store: put a version in keys whose shape changes, or clear them
on deploy.

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

Shared values can be any prop: `o.Share("permissions", ctx => Once(() => LoadPermissions(ctx)))` works too, and
`o.Share("plans", Once(() => plans.All()))` is the equivalent of Laravel's `shareOnce`: the client keeps the value across visits. The top-level shared keys are listed in `"sharedProps": ["errors", "appName", "auth"]` so the client can keep them across navigations. Turn that list off with `o.ExposeSharedPropKeys = false`.

---

## Partial reloads

The client sends `X-Inertia-Partial-Component`, and `X-Inertia-Partial-Data` (`only`) or `X-Inertia-Partial-Except` (`except`). The server then filters props:

- Filtering applies only when the partial component equals the component being rendered.
- Paths are dot paths and match on a `.` boundary: `only: ["user"]` includes `user.name`, and `only: ["user.name"]` includes `user.name` and keeps `user` as a container.
- `except` beats `only`. `Always(...)` ignores both.
- The children of a value produced by a loader are not filtered. Nested literal `InertiaProps` are.
- Loaders of excluded props never run.
- Selecting an array descendant (`rows.1.extra`) reloads the complete array, including sibling fields and nested loaders. A descendant `except` excludes the entire array; `Always` still wins. Use objects keyed by ID for independent updates. Omitted or rescued direct array elements retain their positions as `null`.
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
| Redirect to another origin (scheme, host or port), and the request is not a prefetch | `409` with `X-Inertia-Location: <url>`: the client makes a full visit instead of following it with XHR, which the browser would block. Turn it off with `o.ConvertExternalRedirects = false`. |
| Redirect whose `Location` contains `#`, and the request is not a prefetch | `409` with `X-Inertia-Redirect: <url>` (the client performs a full visit that keeps the fragment) |
| `Location(url)` | `409` with `X-Inertia-Location: <url>` for Inertia requests, a plain `302` otherwise |
| An empty `200` response | redirect back (302, or 303 for PUT/PATCH/DELETE), falling back to the app root (`PathBase/`) |
| `Back()` | `302` to the `Referer` when it is an http(s) URL with the request's own host and port, otherwise to the fallback (default `~/`, the app root including `PathBase`) |

The same-origin rule on `Referer` exists so `Back()` cannot become an open redirect. The scheme is not compared, so it keeps working behind a TLS-terminating proxy. On an HTTPS request an `http` Referer is upgraded to `https`, so going back never downgrades the connection.

`Vary: X-Inertia` is added to every response that passes through the middleware.

**Pages without a handler.** `app.MapInertia("/about", "About", props)` maps GET and HEAD to `Render("About", props)`, like Laravel's
`Route::inertia`. In MVC, `return this.InertiaView(props);` renders the action's own component, `{Controller}/{Action}` by default
(`Admin/Users/Index` in an `Admin` area); `AddInertiaMvc(o => o.ComponentResolver = ctx => ...)` changes the rule.

**The page URL.** The page object's `url` is PathBase, Path and QueryString. Behind a proxy that rewrites paths, set
`o.UrlResolver = ctx => ...` to return the URL the browser sees.

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
  { host: '127.0.0.1', port: Number(process.env.INERTIA_SSR_PORT ?? 13714) },
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
  { host: '127.0.0.1', port: Number(process.env.INERTIA_SSR_PORT ?? 13714) },
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
  { host: '127.0.0.1', port: Number(process.env.INERTIA_SSR_PORT ?? 13714) },
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

The SSR build leaves `node_modules` imports external, so run the server where `node_modules` is installed (a Docker image with `npm ci --omit=dev` is the usual way). Either let the app start it (`o.Ssr.UseNodeProcess()`, below), or run it yourself with a process manager (systemd, pm2) or as a sidecar container (`docker compose` service) and add its URL to `o.Ssr.Urls`. The entries above listen on `INERTIA_SSR_PORT` when it is set, which the app needs to run [several instances](#let-the-app-run-the-node-server).

### 2. Enable it

```csharp
builder.Services.AddInertia(o =>
{
    o.Ssr.Mode = SsrMode.Preferred;                  // Disabled (default), Preferred (fall back to the client) or Required (throw)
    o.Ssr.Urls.Add("http://127.0.0.1:13714");       // the default when empty; add more servers to spread the renders
    o.Ssr.Timeout = TimeSpan.FromSeconds(5);         // total deadline: the wait for a render slot plus the HTTP call
    o.Ssr.MaxPageBytes = 2 * 1024 * 1024;            // default; larger pages render on the client without a call
    o.Ssr.MaxResponseBytes = 16L * 1024 * 1024;     // default, for success and error responses
    o.Ssr.BundlePath = "ssr/ssr.js";                 // skip SSR while this file does not exist (relative to the content root)
    o.Ssr.ExcludePaths.Add("/admin/*");              // never SSR these (exact match, or a trailing * for a prefix), Request.Path without PathBase
});
```

A response can choose its own mode:

```csharp
return Render("Checkout").WithoutSsr();               // always rendered on the client
return Render("Landing").WithSsr(SsrMode.Required);   // fail the request rather than fall back
return Render("Admin/Home").WithSsr(SsrMode.Preferred); // rendered on the server although /admin/* is excluded
```

The global mode decides first: `Disabled` is a kill switch that no response can override. Otherwise the response's own mode wins, then `ExcludePaths`, then the global mode.

SSR runs only for first visits (the HTML response), never for Inertia XHR requests. The page JSON is posted to `{url}/render` and the answer `{ "head": [...], "body": "..." }` is written into the root view: `@inertiaHead` outputs the head tags joined with `\n`, and `@inertia` outputs the body verbatim.

#### When the server fails

In `Preferred` mode every failure logs a warning and the page renders on the client. In `Required` mode it throws `InertiaSsrException` before the first byte of the response is written, so the app's exception handler can still answer. `InertiaSsrException.Type` (and the `inertia.ssr.reason` metric tag) says what happened:

| Type | Cause | Opens the circuit |
|---|---|---|
| `connection` | Connection refused or reset. A refused connection is retried once on another server. | yes |
| `timeout` | `Ssr.Timeout` elapsed. | yes |
| `http` | A non-2xx answer without a structured error, such as a proxy's error page or a redirect. | yes |
| `response` | An empty or malformed answer. | yes |
| `unknown` | Anything else, e.g. a handler that cannot send the request. | yes |
| the server's `type`, else `render` | A structured `{ "error", "type", "hint" }` answer: one page's component failed. | no |
| `response-too-large` | The answer is over `MaxResponseBytes`. | no |
| `overloaded` | No render slot was free within `Concurrency.QueueTimeout`, or the queue was full. | no |
| `circuit-open`, `not-ready`, `page-too-large`, `bundle-missing` | Skipped without a call: every server is paused, no managed process is ready, the page is over `MaxPageBytes`, or the bundle is missing. | no |

Failures are logged with the server's `hint` and source location, cut to 2 KB. Skips are logged at most once every 30 seconds per reason, with the number of skips left out; a missing bundle is not logged on every page.

**Circuit breaker.** After `CircuitBreaker.FailureThreshold` (default 5) failures in a row that open the circuit, a server is skipped for `CircuitBreaker.BreakDuration` (default 10 s). Then one render probes it: success closes the circuit, failure starts another break. A page that fails to render because of its own code never pauses the server. A managed process that restarts and passes its health check closes its circuit at once. `CircuitBreaker.Enabled = false` turns this off.

**Render slots.** Each server takes at most `Concurrency.MaxActive` renders at once (default 8, `0` for no limit) from this app instance, with up to `Concurrency.MaxQueued` (default 32) waiting at most `Concurrency.QueueTimeout` (default 250 ms) for a slot. Renders that find no slot fall back at once instead of piling up. The limits are per app instance: a server behind N instances can get N × `MaxActive` renders.

#### Several SSR servers

```csharp
o.Ssr.Urls.Add("http://ssr-1:13714");
o.Ssr.Urls.Add("http://ssr-2:13714");
```

Each render goes to the server with the fewest renders in flight among those whose circuit is closed (ties rotate). Every server has its own slots, queue and circuit. Only a refused connection is retried on another server, within the same deadline: after a timeout or an error the render may already have run.

**Remote servers.** Page props travel to the renderer, and its HTML goes into your pages unescaped, so it must be trusted. A plain `http://` URL that is not loopback logs a warning at startup; use HTTPS or a private network. The `Inertia.Net.Ssr` HTTP client (`InertiaSsr.HttpClientName`) does not follow redirects and does not use a proxy. Add authentication with a message handler, e.g. `services.AddHttpClient(InertiaSsr.HttpClientName).AddHttpMessageHandler<SsrAuthHandler>()`. If you replace its primary handler, keep `AllowAutoRedirect = false`.

#### Let the app run the Node server

```csharp
o.Ssr.UseNodeProcess();          // also sets Mode = Preferred when it is Disabled
o.Ssr.UseNodeProcess(n =>
{
    n.BundlePath = "ssr/ssr.js";                 // default: Ssr.BundlePath, else the first of ssr/ssr.{js,mjs}, bootstrap/ssr/ssr.{js,mjs}, dist/ssr.{js,mjs}
    n.Executable = "node";                       // default, looked up on PATH
    n.Arguments.Add("--enable-source-maps");     // Node options, passed before the bundle path
    n.Environment["NODE_OPTIONS"] = "--max-old-space-size=512";
    n.WorkingDirectory = null;                   // default: the content root
    n.StartupTimeout = TimeSpan.FromSeconds(10); // default
    n.RestartOnExit = true;                      // default
    n.Instances = 1;                             // default; instance i listens on BasePort + i
    n.BasePort = 13714;                          // default
    n.LivenessInterval = TimeSpan.FromSeconds(5); // default: GET /health on each running instance
    n.LivenessFailureThreshold = 3;              // default: failed checks in a row before the instance is killed and restarted
    n.AllowNetworkExposure = false;              // default: stop an instance that also answers beyond loopback
});
```

A hosted service runs `node {bundle}` (no shell) once per instance when the app starts, logs its stdout (Information) and stderr (Warning) under the `Inertia.Net.Ssr` category, and waits up to `StartupTimeout` for every instance's `GET /health`. Occupied ports are refused before anything is spawned, and `Ssr.Urls` is not used: renders go only to the owned instances, and only once one is healthy. A missing bundle, a missing `node` or a startup timeout is logged and pages render on the client; in `Required` mode app startup fails with `InertiaSsrException` (`Type = "startup"`). An instance that exits is restarted after 1 s, doubling up to 30 s; one that stops answering its liveness check (a blocked event loop keeps the process alive) is killed and restarted the same way. Shutdown cancels supervision and stops the owned process trees without requesting `/shutdown`. Nothing is spawned while the Vite dev server runs. For external or remote servers, set `Mode` and `Urls` without `UseNodeProcess()`.

- **Ports.** Each instance gets its port in `INERTIA_SSR_PORT` (and `NODE_ENV=production` unless set). The entries in step 1 listen on it. The v3 server (`createServer` in `@inertiajs/core`) has no CLI flag for the port, and a bundle built by `@inertiajs/vite` alone listens on the port from `inertia({ ssr: { port } })`, baked in at build time (default `13714`). Such a bundle only works with one instance on that `BasePort`; for `Instances > 1` use an entry that reads `INERTIA_SSR_PORT`.
- **Keep it on loopback.** The SSR server has no authentication: anyone who can reach it can `POST /render` (CPU) or `POST /shutdown`,
  and its `head` and `body` are written into your pages verbatim, so it must be trusted. Listen on `127.0.0.1` (the entries above do;
  with `@inertiajs/vite` build with `inertia({ ssr: { host: '127.0.0.1' } })`; its default is `0.0.0.0`). Before an instance takes renders,
  Inertia.Net tries the machine's non-loopback addresses on its port. If the instance answers there, it is stopped, not restarted, and an
  error is logged (`Stopped the SSR server: it also accepts connections on ...`). With `AllowNetworkExposure = true` it keeps running with a warning.
- **Orphans.** A graceful shutdown (Ctrl+C, SIGTERM, `docker stop`, IIS recycle) stops Node. If the .NET process is killed outright or crashes hard, the OS does not take Node with it; the next start rejects the occupied port. Use a process manager or container for hard-crash cleanup.

### 3. Development

With the Vite hot file present (Development only), or `o.Vite.DevServerUrl` set, Inertia.Net posts to `{viteDevUrl}/__inertia_ssr` instead. That endpoint is served by the `@inertiajs/vite` plugin, so you need no built bundle and no Node server:

```ts
// vite.config.ts, add to plugins
import inertia from '@inertiajs/vite'
inertia({ ssr: { entry: 'resources/js/ssr.tsx', host: '127.0.0.1', port: 13714 } })
```

`BundlePath` is ignored while the dev server runs.

### 4. Health check

```csharp
builder.Services.AddHealthChecks().AddInertiaSsr();   // name "inertia-ssr"; optional failureStatus, tags, timeout
app.MapHealthChecks("/health");
```

The check reports what the renders have already shown, without calling the SSR servers: healthy while SSR is disabled, goes through the Vite dev server, or every server can take renders; degraded while some servers' circuits are open or some managed instances are not ready; the registration's failure status (default unhealthy) when none can. Its data lists each server's state (`closed`, `half-open`, `open` or `not-ready`). A server that went down is reported healthy until renders fail.

### 5. Observability

Inertia.Net publishes metrics on the `Inertia.Net` meter and traces on the `Inertia.Net` activity source (`InertiaDiagnostics.MeterName`, `InertiaDiagnostics.ActivitySourceName`):

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(InertiaDiagnostics.MeterName))
    .WithTracing(t => t.AddSource(InertiaDiagnostics.ActivitySourceName));
```

| Instrument | Type | Tags |
|---|---|---|
| `inertia.page.duration` (s) | histogram | `inertia.response` (`json`, `html`), `inertia.partial` |
| `inertia.page.size` (By) | histogram | `inertia.response` |
| `inertia.ssr.duration` (s) | histogram | `inertia.ssr.outcome` (`rendered`, `fallback`) |
| `inertia.ssr.queue.duration` (s) | histogram | |
| `inertia.ssr.active` | up-down counter | |
| `inertia.ssr.outcomes` | counter | `inertia.ssr.outcome` (`rendered`, `fallback`, `error`), `inertia.ssr.reason` (`ok`, `cache`, `stale` or a type from the table above) |
| `inertia.ssr.response.size` (By) | histogram | |
| `inertia.ssr.circuit.state` | gauge (0 closed, 1 half-open, 2 open) | `inertia.ssr.endpoint` (the index in `Urls` or of the instance, or `dev`) |
| `inertia.ssr.circuit.transitions` | counter | `inertia.ssr.endpoint`, `to` |
| `inertia.ssr.process.restarts` | counter | `cause` (`exit`, `liveness`) |
| `inertia.props.rescued` | counter | |

Tag values are bounded: no URLs, component names or user data. The `inertia.render` activity carries `inertia.component`; the `inertia.ssr` activity carries `inertia.ssr.endpoint`, `inertia.ssr.outcome` and `inertia.ssr.reason`. Nothing is recorded unless a listener is attached.

### 6. Response cache

A render depends only on the page JSON, so a page that many visitors see byte for byte the same (a landing page, a catalog or an
article) can reuse its HTML instead of calling the SSR server again:

```csharp
builder.Services.AddInertia(o =>
{
    o.Ssr.Cache.Duration = TimeSpan.FromMinutes(5);   // every server-rendered page; default Zero (off)
    o.Ssr.Cache.StaleFor = TimeSpan.FromMinutes(30);  // then served stale while it renders again; default Zero
    o.Ssr.Cache.MaxBytes = 64 * 1024 * 1024;          // default; least recently used renders are evicted beyond it
});

return Render("Pricing", props).WithSsrCache(TimeSpan.FromHours(1));   // one page; WithSsrCache(TimeSpan.Zero) opts out
return Render("Pricing", props).WithSsrCache(TimeSpan.FromHours(1), staleFor: TimeSpan.FromDays(1));
```

The key is the SHA-256 of the page JSON (component, props, url, version and the rest), so a new deploy (another asset version) or
different props never reuse a stale render. Pages with per-user props make one entry per user: cache the pages that are shared. A hit
is counted as `inertia.ssr.outcomes{inertia.ssr.outcome=rendered, inertia.ssr.reason=cache}` and bypasses the render slots and circuit
breakers. Nothing is cached while the Vite dev server renders.

Concurrent misses for the same page share one fill per app instance. Waiting counts against `Ssr.Timeout`; cancelling a waiting
request does not cancel the request filling the entry. If that fill fails or is cancelled, a waiting request can retry.

With `StaleFor`, a render past its `Duration` is still served, counted with `inertia.ssr.reason=stale`, and the page is rendered again
after that response has been sent, as for [stale props](#cached-props): one refresh per page and app instance at a time, and a failed
one keeps the stale render. `IInertiaCache.ClearSsrAsync()` removes every render, for an SSR bundle deployed without a new asset
version. Renders are kept in the same [`IInertiaCacheStore`](#cached-props) as props; `MaxBytes` bounds the default store only.

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

**Prefetching.** Page components resolved with lazy `import.meta.glob` (the `@inertiajs/vite` default) are separate chunks that the
browser downloads only when a page needs them. `o.Vite.Prefetch` downloads them after the page has loaded, at low priority, so the next
navigation does not wait (Laravel's `Vite::useWaterfallPrefetching`):

```csharp
o.Vite.Prefetch = VitePrefetch.Waterfall;   // PrefetchConcurrency (default 3) at a time; VitePrefetch.Aggressive starts all at once
```

The tags get one small inline script with the list of chunks the entries can import later (dynamic imports, then their static imports
and CSS), minus those already loaded. It is rendered once per entry set like the other tags.

### Content Security Policy

With a nonce-based CSP, set `o.CspNonce` to the request's nonce. Every script and stylesheet tag Inertia.Net writes then carries it:
the page `<script data-page>`, the Vite tags, the React refresh preamble and the prefetch script (whose links get it too). The MVC tag
helpers follow the same option.

```csharp
builder.Services.AddInertia(o => o.CspNonce = ctx => ctx.Items["csp-nonce"] as string);

app.Use((ctx, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    ctx.Items["csp-nonce"] = nonce;
    ctx.Response.Headers.ContentSecurityPolicy = $"script-src 'nonce-{nonce}' 'strict-dynamic'; object-src 'none'; base-uri 'none'";
    return next(ctx);
});
```

The nonce must be base64 or base64url (anything else throws). The cached tags are split where the nonce goes, so a nonce costs one copy
of the tag bytes per request and no re-rendering.

---

## CSRF and antiforgery

The Inertia client reads the `XSRF-TOKEN` cookie and sends its value as the `X-XSRF-TOKEN` header on every request.
`UseInertiaAntiforgery()` issues that cookie and validates it, so one call protects every form:

```csharp
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);   // "X-XSRF-TOKEN"

app.UseAuthentication();                 // if the app signs users in: the token is bound to the signed-in user
app.UseInertia();
app.UseInertiaAntiforgery();
```

- **Issue.** Page loads (GET/HEAD requests that are Inertia requests or accept `text/html`) get the request token in a script-readable,
  essential cookie (`HttpOnly` off, `SameSite=Lax`, `Path=/`, `Secure` per `SecurePolicy`). Essential means a cookie-consent policy
  (`CheckConsentNeeded`) does not withhold it, which would otherwise make every form submission fail.
- **Validate.** Every unsafe request (anything but GET, HEAD, OPTIONS and TRACE), with a JSON or a form body, Inertia or not, is checked
  with `IAntiforgery.ValidateRequestAsync`. A plain cross-site HTML form post is rejected as well as a forged XHR. It also sets the
  antiforgery feature, so form-binding endpoints work without `app.UseAntiforgery()`.
- **Reject.** A missing or wrong token gets **`419`** (page expired, as in Laravel); the handler does not run.
- **Opt out** per endpoint with `.DisableAntiforgery()` (webhooks, test hooks), or for whole areas with `ShouldValidate`.

```csharp
app.UseInertiaAntiforgery(o =>
{
    o.CookieName = "XSRF-TOKEN";                                  // default
    o.SecurePolicy = CookieSecurePolicy.Always;                   // default SameAsRequest; see "Deploying behind a proxy"
    o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/api");   // e.g. bearer-token APIs
    // Instead of a bare 419, send the user back with a message (UseInertia() must run before this middleware):
    o.OnRejected = (ctx, error) => Back().WithFlash("error", "The page expired, please try again.");
});
```

**Lazy refresh.** By default every page load issues a fresh token, and antiforgery marks that response `Cache-Control: no-cache, no-store`
(which also keeps the page out of the browser's back/forward cache). `o.CookieRefresh = XsrfCookieRefresh.Lazy` keeps a token that still
validates: such responses get no `Set-Cookie` and stay cacheable, and a token issued before a sign-in or sign-out is replaced on the next
page load. Validating costs about what issuing does, so the gain is in the responses, not in CPU. Set your own `Cache-Control` on
pages with private data.

A session that expires or a sign-in that changes the user invalidates the token in an open tab; the next page load issues a new one.
Returning `Back()` from `OnRejected` gives that tab a fresh token and a message instead of the client's error dialog.

MVC: `UseInertiaAntiforgery()` covers controllers too. `[AutoValidateAntiforgeryToken]` as a global filter still works, but is not needed.
FastEndpoints: covered by the middleware; FastEndpoints' own `EnableAntiforgery()` is not needed for Inertia forms.

**Issue only.** `UseInertiaAntiforgeryCookie(cookieName = "XSRF-TOKEN")` (the 1.0 API) only writes the cookie; you then validate with
`app.UseAntiforgery()` (form bodies only) and your own check for JSON bodies (`await antiforgery.ValidateRequestAsync(context)`).
Prefer `UseInertiaAntiforgery()`.

---

## Deploying behind a proxy

Behind a load balancer or reverse proxy that terminates TLS, Kestrel sees plain HTTP unless the scheme is forwarded. Without it:
the state and `XSRF-TOKEN` cookies are not marked `Secure`, and the version-mismatch `409` sends an `http://` `X-Inertia-Location`.

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // o.KnownProxies / o.KnownNetworks: list your proxy, or headers from anyone else are ignored
});

app.UseForwardedHeaders();               // first
app.UseInertia();
```

When the scheme cannot be forwarded, force the cookies:

```csharp
builder.Services.AddInertia(o => o.State.CookieSecurePolicy = CookieSecurePolicy.Always);
app.UseInertiaAntiforgery(o => o.SecurePolicy = CookieSecurePolicy.Always);
```

On several instances, share the Data Protection key ring (`AddDataProtection().PersistKeysTo...`): the state cookie and the antiforgery
tokens are protected with it. Keep the SSR server on loopback on each instance (see [Server-side rendering](#server-side-rendering)).

### Compression and asset caching

Page JSON and HTML compress five to ten times. Compress at the proxy, or in the app (the samples and the template do):

```csharp
builder.Services.AddResponseCompression();   // Brotli and gzip; Zstandard too from .NET 11
app.UseResponseCompression();                // before UseStaticFiles and UseInertia
```

Over HTTPS the middleware compresses only with `o.EnableForHttps = true`. Review BREACH for your pages first: Inertia.Net keeps the
antiforgery token in a cookie, not in the body, but a page that reflects request input next to a secret is still exposed.

Vite puts a content hash in every built file name, so the files under `build/assets` can be cached for good:

```csharp
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c =>
    {
        if (c.Context.Request.Path.StartsWithSegments("/build/assets"))
        {
            c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});
```

`app.MapStaticAssets()` does the same with build-time Brotli when the client is built before `dotnet publish`, so the files are in
`wwwroot` when the static web assets manifest is generated.

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

**8. A smaller binary.** The SSR client never uses HTTP/3, so it can be trimmed out:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.Net.SocketsHttpHandler.Http3Support" Value="false" Trim="true" />
</ItemGroup>
```

Cached props, the SSR cache, nonces, prefetching, `MapInertia` and the DevTools recorder are all AOT safe and covered by the smoke test.

The reference app is [tests/Inertia.Net.AotSmoke](tests/Inertia.Net.AotSmoke/Program.cs). Run it end to end with `bash eng/aot-smoke.sh` (Linux, needs clang and zlib).

---

## TypeScript types

Inertia.Net can write TypeScript declarations for your typed page props, the shared props and the flash data. They follow the app's JSON
contracts (the HTTP JSON options and their source-generated contexts), so the names, nullability and enum values are those the page JSON
holds:

```csharp
builder.Services.AddInertia(o =>
{
    o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";   // written when the app starts in Development, only when it changed
    o.TypeScript.Page<UsersPage>("Users/Index").Page<HomePage>("Home");
    o.TypeScript.FlashData<Flash>();                             // optional; SharedProps<T>() too
});

public sealed record UsersPage(List<User> Users, [property: InertiaPropType(typeof(UserStats))] InertiaProp Stats);
```

```ts
// ClientApp/types/inertia.d.ts (excerpt)
export interface InertiaPages {
  'Users/Index': UsersPage
  Home: HomePage
}
export interface UsersPage {
  users: User[]
  stats?: UserStats
}
declare module '@inertiajs/core' {
  export interface InertiaConfig {
    sharedPageProps: SharedProps
    flashDataType: FlashData
    errorValueType: string
  }
}
```

```tsx
import type { InertiaPages } from '../../types/inertia'
export default function Index({ users, stats }: InertiaPages['Users/Index']) { /* ... */ }
```

- **Shared props** are inferred from the values passed to `o.Share(...)` (dot keys nest); factories and per-request shares are `unknown`.
  `o.TypeScript.SharedProps<T>()` replaces the inference with a type. The `InertiaConfig` augmentation types `usePage().props` and `flash`.
- **Prop members.** An `InertiaProp` member is optional (it can be deferred, optional or rescued) and `unknown`, unless
  `[InertiaPropType(typeof(T))]` names its value type.
- **Mapping.** Numbers are `number` (`number | bigint` for 64-bit and larger with `PreserveBigIntegers`), dates, GUIDs and `byte[]`
  are `string`, enums are unions of their JSON values (string names with a string enum converter), dictionaries are `Record<string, T>`,
  polymorphic types are unions tagged with their discriminator, and nullable members get `| null`.
- Types missing from the JSON metadata (under Native AOT, register them in your context) are `unknown`, with a warning in the log.
  Commit the file, so `tsc` passes in CI without running the app.

## DevTools

The [Inertia DevTools](https://inertiajs.com/docs/v3/advanced/devtools) browser extension shows every visit, its props, headers and the
page state. Inertia.Net implements its server protocol:

```csharp
builder.Services.AddInertiaDevTools();   // records in Development only
app.UseInertia();
app.MapInertiaDevTools();                // GET /_inertia/devtools/entries/{id} and /entries
```

Each request through `UseInertia()` gets an `X-Inertia-Devtools-Id` and an entry in memory (per browser tab, at most
`MaxEntriesPerTab` and `MaxEntries`, for `MaxAge`): request type, timings, headers, bodies, the page object and its props metadata. The
first page carries the discovery tag the extension reads. Values of names containing `password`, `secret`, `token`, `authorization`,
`cookie` or `api_key` are replaced with `[REDACTED]` in props, bodies and headers; add names with `RedactedKeys`. Nothing leaves the app.

Outside Development nothing is recorded unless `Enabled = true`, and the read API answers only requests that `Authorize` accepts
(`404` otherwise). With `Pages.Directory` set, entries link the component to its file.

## Development checks

`o.Pages.Directory = "ClientApp/pages"` makes `Render` throw in Development when the component has no page file, naming the closest
one (`'Users/Indx' was not found in 'ClientApp/pages' ... Did you mean 'Users/Index'?`). Names match exactly, as the client's resolver
does, even on case-insensitive file systems. `o.Pages.Extensions` lists the file types (`.tsx`, `.jsx`, `.ts`, `.js`, `.vue`, `.svelte`
by default). Outside Development there is no check and no file access.

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

Benchmarks cover page rendering, partial reloads, deferred props, SSR, caching and state storage. These controlled microbenchmarks are not production throughput estimates.

v1.5.2 allocates less per render than v1.5.1 (exact allocation probes, same fixtures):

| Workload | v1.5.1 | v1.5.2 |
|---|---:|---:|
| JSON, 20 props | 2,152 B | 1,736 B |
| Typed POCO page | 1,968 B | 1,552 B |
| Partial reload | 984 B | 568 B |
| HTML, 20 props | 2,216 B | 1,800 B |
| 20 parallel loaders | 6,496 B | 3,992 B |

In two paired runs, partial reloads and pages with parallel loaders were faster in both; ordinary JSON and HTML rendering showed no
clear change. Those timings come from a laptop that cannot resolve differences under about 10%; see
[the benchmark results](tests/Inertia.Net.Benchmarks/RESULTS.md) for intervals and raw samples.

Published NuGet v1.5.1 versus v1.5.0, using identical fixtures across 32 paired rounds:

| Workload | v1.5.0 median | v1.5.1 median | Paired speed improvement | Allocated bytes/op, v1.5.0 -> v1.5.1 |
|---|---:|---:|---:|---:|
| JSON, 20 props | 74.929 us | 75.110 us | no clear change | 2,760 -> 2,152 |
| Partial reload | 31.758 us | 31.885 us | no clear change | 1,592 -> 984 |
| HTML, 20 props | 79.746 us | 77.909 us | no clear change | 2,824 -> 2,216 |
| Cache write, 1 KiB | 1.814 us | 1.034 us | 43.3% faster | 4,880 -> 1,240 |
| Cache write, 1 MiB | 1,131.886 us | 485.719 us | 56.5% faster | 4,195,405 -> 1,048,871 |
| Dictionary, 1,000 integer keys | 1,136.159 us | 1,025.162 us | 8.1% faster | 216,992 -> 177,008 |
| 32 simultaneous cold-cache requests | 95.837 ms | 12.464 ms | 87.0% faster | 132,696 -> 83,784 |

The cold-cache fixture uses four backend slots and 10 ms simulated I/O; v1.5.0 runs 32 loaders and v1.5.1 runs one. Ordinary JSON,
HTML and partial rendering show no statistically clear release-to-release slowdown. Percentages summarize within-round ratios,
so they can differ from ratios of the separate time medians. The previously reported roughly 3% cost came from an incremental
memory experiment against an already optimized intermediate version, not the published v1.5.0 release.

The separate incremental GC/RSS experiment found fewer collections and shorter total GC pauses, with no RSS reduction; its
baseline differs from this published-release comparison. Full workloads, confidence intervals, package hashes, raw paired samples
and baseline definitions are in [the benchmark results](tests/Inertia.Net.Benchmarks/RESULTS.md).

To measure your build, run `dotnet run -c Release -- --filter '*'` from `tests/Inertia.Net.Benchmarks`. Targeted probes are available with `--render-probe`, `--ssr-probe`, `--mvc-probe`, `--payload-probe` and `--cache-probe`.

**Payload size.** The page JSON is fully buffered before the first byte is sent, so a failing loader never produces a half-written 200.
Each concurrent request therefore holds its whole page in memory (pooled, and sized from the component's recent pages). Keep pages well
under a megabyte: paginate (`Scroll`), defer (`Defer`) or load on demand (`Optional`) large data. `Ssr.MaxPageBytes` keeps larger pages
off the SSR server and `Ssr.MaxResponseBytes` bounds its answer; neither limits the props.

Design notes:

- **Single pass.** One recursive walk filters props, runs loaders, writes JSON to a `Utf8JsonWriter` and collects the metadata. There is no intermediate object tree.
- **Pooled buffers.** The page is written to a pooled `IBufferWriter<byte>`, and the path tracker is a pooled `char[]`. A path becomes a string only when metadata needs it. The buffer also means a failing loader never produces a half-written 200.
- **Version check before the handler.** A stale client costs one header comparison and a 409.
- **No per-request mutable singletons.** Per-request state lives in an `InertiaFeature` on `HttpContext.Features`, so concurrent requests cannot leak history or flash settings into one another.
- **Plain DTOs take the fast path.** A POCO, list or typed dictionary is walked only when its type can hold `InertiaProp` values (`InertiaProp`, `object` or dictionary members/elements; cached per type). Everything else is one `Serialize` call.
- **UTF-8 throughout.** SSR output and the MVC tag helpers write UTF-8 bytes; nothing large is materialized as a .NET string.
- **Cached assets.** The Vite manifest is parsed once, the root template is parsed once into pre-encoded UTF-8 segments, and the production tags are pre-rendered per entry set and path base. The version is a precomputed string compared ordinally. A manifest, template or SSR bundle that is missing at startup is re-checked at most once per second, so files deployed after the app starts are picked up without a restart.
- **Sync loaders stay sync.** `Func<T>` loaders are not wrapped in closures.

Most per-request allocation in a real handler is the props you build, not the render.

---

## Configuration reference

### `AddInertia(o => ...)`: `InertiaOptions`

| Option | Default | Description |
|---|---|---|
| `RootView` | `"app.html"` | Root template, relative to the content root (a Razor view name with `UseRazorRootView`). Paths outside the content root are refused; never take it from the request. |
| `RootElementId` | `"app"` | Id of the root element and the `data-page` script. |
| `Version` | `null` | Asset version. `null` = hash of the Vite manifest, or `""` without one. |
| `VersionResolver` | `null` | `Func<HttpContext, string?>` per-request version; wins over `Version` when non-null. |
| `EncryptHistory` | `false` | Encrypt browser history state of every page. |
| `WithAllErrors` | `false` | Send every validation message per field instead of the first. |
| `PreserveBigIntegers` | `false` | Emit integers beyond ±(2^53−1) as `{"$bigint":"..."}` and set `preserveBigIntegers`. |
| `ExposeSharedPropKeys` | `true` | List top-level shared keys in `sharedProps`. |
| `CspNonce` | `null` | `Func<HttpContext, string?>`: the request's CSP nonce, written on every script and stylesheet tag. |
| `ConvertExternalRedirects` | `true` | Turn an Inertia request's redirect to another origin into `409` + `X-Inertia-Location`. |
| `UrlResolver` | `null` | `Func<HttpContext, string>`: the page `url`. |
| `Pages.Directory` / `Pages.Extensions` | `null` / `.tsx .jsx .ts .js .vue .svelte` | Development check that a rendered component has a page file. |
| `TypeScript.OutputPath`, `.Page<T>(component)`, `.SharedProps<T>()`, `.FlashData<T>()` | `null` | TypeScript declarations, written in Development. |
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
| `Prefetch` | `None` | `Waterfall` or `Aggressive`: prefetch the code-split chunks after load. |
| `PrefetchConcurrency` | `3` | Chunks `Waterfall` downloads at a time. |

### `o.Ssr`: `SsrOptions`

| Option | Default | Description |
|---|---|---|
| `Mode` | `Disabled` | `Disabled`, `Preferred` (fall back to client rendering on failure) or `Required` (throw `InertiaSsrException`). `Disabled` cannot be overridden per response. |
| `Urls` | empty (`http://127.0.0.1:13714`) | SSR server base URLs (`/render`). Several spread the renders. Not used with `UseNodeProcess()`. |
| `Timeout` | `5 s` | Total deadline per render: the wait for a slot plus the HTTP call. `Timeout.InfiniteTimeSpan` disables it. |
| `MaxResponseBytes` | `16 MiB` | Limit on actual response bytes, including errors and chunked responses. Oversized responses fall back, or throw with type `response-too-large`. |
| `MaxPageBytes` | `2 MiB` | Pages whose JSON is larger render on the client without a call (type `page-too-large`). |
| `BundlePath` | `null` | Skip SSR while this file is missing, unless the Vite dev server runs. A missing bundle is re-checked at most once per second; in production a present one is not checked again. |
| `ExcludePaths` | empty | Request paths never rendered on the server (unless the response calls `WithSsr`): exact, or a trailing `*` for a prefix. Must start with `/`. |
| `Concurrency.MaxActive` | `8` | Renders in flight per server from this app instance; `0` for no limit. |
| `Concurrency.MaxQueued` | `32` | Renders waiting for a slot per server; further renders fall back at once. |
| `Concurrency.QueueTimeout` | `250 ms` | How long a render waits for a slot. |
| `CircuitBreaker.Enabled` | `true` | Pause servers that keep failing. |
| `CircuitBreaker.FailureThreshold` | `5` | Consecutive connection, timeout, HTTP or malformed-response failures that open a server's circuit. |
| `CircuitBreaker.BreakDuration` | `10 s` | How long an open circuit skips the server before one render probes it. |
| `Cache.Duration` | `Zero` (off) | Reuse a render for this long when the page JSON is the same. Per response: `WithSsrCache(duration)`. |
| `Cache.StaleFor` | `Zero` | Then serve it for this long more while the page renders again after the response. Per response: `WithSsrCache(duration, staleFor)`. |
| `Cache.MaxBytes` | `64 MiB` | Size of the default store's render cache; least recently used renders are evicted. |
| `UseNodeProcess(configure?)` | off | Start and supervise Node SSR processes with the app (sets `Mode = Preferred` when `Disabled`). Options below. |

Per response: `Render(...).WithSsr(SsrMode)` and `.WithoutSsr()`.

### `o.Ssr.UseNodeProcess(n => ...)`: `SsrNodeProcessOptions`

| Option | Default | Description |
|---|---|---|
| `BundlePath` | `Ssr.BundlePath`, else probed | Bundle to run, relative to the content root. Probes `ssr/ssr.{js,mjs}`, `bootstrap/ssr/ssr.{js,mjs}`, `dist/ssr.{js,mjs}`. |
| `Executable` | `"node"` | Node executable (path, or name on `PATH`). |
| `Arguments` | empty | Node options, passed before the bundle path. |
| `Environment` | empty | Extra environment variables (the app's environment is inherited; `INERTIA_SSR_PORT` and `NODE_ENV=production` are added). |
| `WorkingDirectory` | content root | Process working directory. |
| `StartupTimeout` | `10 s` | How long startup waits for every instance's `/health`; then a warning (or `InertiaSsrException` in `Required` mode). |
| `RestartOnExit` | `true` | Restart after an unexpected exit or a failed liveness check, 1 s doubling to 30 s. |
| `Instances` | `1` | Node processes to run (1 to 64). Instance `i` listens on `127.0.0.1:{BasePort + i}` and gets that port in `INERTIA_SSR_PORT`. |
| `BasePort` | `13714` | Port of the first instance. |
| `LivenessInterval` | `5 s` | How often each running instance is checked with `GET /health` (2 s timeout). |
| `LivenessFailureThreshold` | `3` | Failed checks in a row after which the instance is killed and restarted. |
| `AllowNetworkExposure` | `false` | Keep an instance that also answers on a non-loopback address (with a warning) instead of stopping it. |

### `o.Props`: `InertiaPropsOptions`

| Option | Default | Description |
|---|---|---|
| `MaxConcurrency` | `4` | Loaders marked `.Parallel()` that run at once per render. Must be positive. |

### `o.State`: `InertiaStateOptions`

| Option | Default | Description |
|---|---|---|
| `CookieName` | `".Inertia.State"` | Cookie name (cookie store) or session key (session store). |
| `CookieSecurePolicy` | `SameAsRequest` | When the state cookie is `Secure`. `Always` behind a TLS-terminating proxy that does not forward the scheme. |
| `UseSession()` | off | Use `ISession` instead of the encrypted cookie. The session is only loaded when the request has a session cookie. |
| `AddInertiaMvc(o => o.UseTempDataStateStore())` | off | Use MVC TempData (cookie or session provider) instead of the encrypted cookie. Inertia.Net.Mvc package. |

### Middleware, endpoint and result API

| API | Description |
|---|---|
| `services.AddInertia(configure?)` | Registers services and options. |
| `app.UseInertia()` | The protocol middleware. Before the endpoints, after `UseSession()` when the session store is used. |
| `app.UseInertiaAntiforgery(configure?)` | Issues the `XSRF-TOKEN` cookie and validates every unsafe request (`419` on failure). Options: `CookieName`, `SecurePolicy`, `ShouldValidate`, `OnRejected`, `CookieRefresh` (`Always`, `Lazy`). |
| `app.MapInertia(pattern, component, props?)` | GET and HEAD endpoint that renders the component. |
| `services.AddInertiaDevTools(configure?)` / `app.MapInertiaDevTools()` | DevTools recorder (`Enabled`, `Authorize`, `MaxEntriesPerTab`, `MaxEntries`, `MaxAge`, `MaxBodyBytes`, `RedactedKeys`) and its read API. |
| `app.UseInertiaAntiforgeryCookie(cookieName = "XSRF-TOKEN")` | Only writes the antiforgery request token to a script-readable cookie; validation is up to you. |
| `endpoint.WithInertiaValidation()` | Minimal API: validation failures redirect back with errors. Endpoint or group. |
| `endpoint.WithInertiaPrecognition()` | Minimal API: `WithInertiaValidation()` plus Precognition. |
| `healthChecks.AddInertiaSsr(name = "inertia-ssr", failureStatus?, tags?, timeout?)` | SSR health check, from the gateway's state (no call to the servers). |
| `mvc.AddInertiaMvc(o => o.UseRazorRootView("App"))` | MVC filter, optional Razor root view, `ComponentResolver` for `this.InertiaView(props)`. |
| `config.UseInertia(configure?)` | FastEndpoints: validation redirect and Precognition (`app.UseFastEndpoints(c => ...)`). |
| `Render(component, props?)` | `InertiaResult`: `.WithStatusCode(int)`, `.WithRootView(string)`, `.WithViewData(key, value)`, `.WithSsr(SsrMode)`, `.WithoutSsr()`, `.WithSsrCache(TimeSpan[, staleFor])`. |
| `Location(url)` | `InertiaLocationResult`. |
| `Back(fallback = "~/")` | `InertiaBackResult`: `.WithErrors(errors, bag = "default")`, `.WithFlash(key, value)`. |
| `httpContext.Inertia()` | `InertiaFeature`: `Share`, `Flash`, `WithErrors`, `ClearHistory`, `EncryptHistory(bool = true)`, `PreserveFragment`. |
| `Send.InertiaAsync(component, props?)`, `Send.InertiaLocationAsync(url)`, `Send.InertiaBackAsync(fallback = "~/")` | FastEndpoints `Send` extensions. |

Extension points: `IInertiaRootView`, `IInertiaStateStore`, `ViteAssets`, `InertiaRequest` (the parsed headers), `InertiaHeaders` (header name constants), `InertiaSsrException`, `InertiaSsr.HttpClientName` (the SSR HTTP client), `InertiaDiagnostics` (meter and activity source names).

---

## Migrating from other .NET adapters

The API is close to the Laravel adapter's names. The table maps the typical InertiaCore-style API (kapi2289/InertiaCore and its forks). Check names against the version you use.

| Other adapters | Inertia.Net |
|---|---|
| `Inertia.Render("Users/Index", new { users })` | `Render("Users/Index", new InertiaProps { ["users"] = users })`, or a typed props object |
| `return Inertia.Render(...)` in a controller (`Task<IActionResult>`) | `return Render(...)` as `IResult` (MVC executes it) |
| `Inertia.Lazy(() => ...)` / `LazyProp` | `Optional(() => ...)` (never on first load) or `Prop(() => ...)` (lazy, included by default) |
| `Inertia.Defer(...)` / `Inertia.Defer(..., "group")` | `Defer(..., group: "group")` |
| `Inertia.Always(...)` | `Always(...)` |
| `Inertia.Merge(...)` / `DeepMerge` | `Merge(...)`, `DeepMerge(...)`, plus `.Prepend()`, `.MatchOn()` |
| (not supported) | `Once(...)`, `Scroll(...)`, `.Rescue()`, `.Parallel()`, `.Cache(key, duration)` |
| `Inertia.Share("key", value)` | `o.Share("key", value)`, `o.Share("key", ctx => ...)`, or `ctx.Inertia().Share(...)` |
| `Inertia.Location(url)` | `Location(url)` |
| `Inertia.Back()` | `Back()` / `Back(fallback)` |
| `Inertia.ClearHistory()`, `Inertia.EncryptHistory()` | `ctx.Inertia().ClearHistory()`, `ctx.Inertia().EncryptHistory()` (per request, no shared state) |
| `Inertia.Html(...)` / `Inertia.Head` in `_Layout.cshtml`, or `AddInertia(o => o.RootView = "~/Views/App.cshtml")` | `app.html` with `@inertia`, `@inertiaHead`, `@vite(...)`, or the Razor tag helpers `<inertia />`, `<inertia-head />`, `<vite />` |
| Flash via `TempData` or session | `ctx.Inertia().Flash(...)`, `Back().WithFlash(...)` (encrypted cookie by default) |
| `ModelState` errors in session | MVC: `AddInertiaMvc()`; Minimal API: `.WithInertiaValidation()`; FastEndpoints: `c.UseInertia()` |
| `Version` option or manifest hashing per request | `o.Version`, `o.VersionResolver`, or the cached manifest hash |
| SSR options (`UseSsr`, `SsrUrl`) | `o.Ssr.Mode`, `o.Ssr.Urls`, `o.Ssr.ExcludePaths`, `Render(...).WithSsr(...)` |

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

**Form submissions fail with `419`.**
`UseInertiaAntiforgery()` rejected a request without a valid `X-XSRF-TOKEN`. Check that the page was loaded through the middleware (it sets
the `XSRF-TOKEN` cookie), that the cookie reaches the browser (`Secure` on plain HTTP is dropped; see [Deploying behind a proxy](#deploying-behind-a-proxy)),
that all instances share the Data Protection key ring, and that `UseAuthentication()` runs before it. A sign-in or sign-out changes the
token: reload the page, or return `Back()` from `OnRejected`. Exempt webhooks and token-authenticated APIs with `.DisableAntiforgery()` or `ShouldValidate`.

**`The Inertia page component '...' was not found in '...'` (Development).**
`o.Pages.Directory` is set and the component passed to `Render(...)` has no page file there. Names match exactly, as the client's page
resolver does, even on a case-insensitive file system; the message suggests the closest page. Fix the name, or check
`Pages.Extensions` when your pages use another extension. The check never runs outside Development.

**A redirect to another site turns into a `409`.**
That is `ConvertExternalRedirects` (on by default): an Inertia request cannot follow a redirect to another origin, so it gets
`409` + `X-Inertia-Location` and the client does a full visit. If it happens for your own host, the app is behind a proxy that does
not forward the scheme (see [Deploying behind a proxy](#deploying-behind-a-proxy)), or use relative redirects.
`o.ConvertExternalRedirects = false` disables this conversion.

**`The Inertia root view '...' resolves outside the content root`.**
`RootView` and `WithRootView(...)` take a path relative to the content root; absolute paths and `..` are refused. Never derive them from request data.

**The page keeps reloading, or a request loops with `409`.**
A 409 means the asset version differs between the client and the server. A loop means it never converges: usually the version changes on every request (a `VersionResolver` that returns a new value each time, or a manifest regenerated by a file watcher), or different instances behind a load balancer serve different builds. Use a stable version string. In Development the manifest hash changes after each `vite build`, which is expected once.

**The dev server tags do not appear.**
The hot file is read only when `ASPNETCORE_ENVIRONMENT=Development`. Check that `wwwroot/hot` exists and contains `http://localhost:5173`, or set `o.Vite.DevServerUrl`. Remember `base: '/'` for the dev server in `vite.config` (the `command === 'build'` check in the example).

**`The Inertia root template '...app.html' was not found` or a missing `@inertia` token.**
`RootView` is relative to the **content root**. Copy `app.html` to the output and publish directories (`CopyToOutputDirectory`, `CopyToPublishDirectory`). The template must contain `@inertia`.

**Flash data or errors disappear, or a warning says the cookie is over 4 KB.**
The state cookie is limited to about 4 KB by browsers. Flash less data, or use `o.State.UseSession()`. If the app runs on several instances, share Data Protection keys. If the cookie is never sent back, check the cookie `Secure` flag on HTTP behind a proxy that does not forward `X-Forwarded-Proto` (see [Deploying behind a proxy](#deploying-behind-a-proxy)).

**Validation errors do not show up (Minimal API).**
Use `.WithInertiaValidation()` on the endpoint. Without it, the built-in `AddValidation()` filter returns a `400` problem details that the Inertia client cannot use. Make sure the model types are `public` (see [Native AOT](#native-aot)). The request must be an Inertia request and not a GET.

**Validation keys do not match my field names.**
Keys go through the JSON naming policy per segment (`Address.Street` becomes `address.street`). In MVC the policy is the one in the MVC `JsonOptions`. The page itself is written with the HTTP `JsonOptions`: keep both policies aligned.

**`InvalidOperationException: InertiaProp values can only be serialized by Inertia.Net`.**
An `InertiaProp` ended up somewhere Inertia.Net does not walk: a `Flash(...)` value, a read-only dictionary type that does not implement `IDictionary`, or a type with a custom converter. Lists, typed dictionaries (`Dictionary<TKey, TValue>` with any key type; keys are written as System.Text.Json writes them), `object` members and `IDictionary<string, object?>` are walked. Put props in `InertiaProps`, or declare the member as `InertiaProp` (or `object`) on the typed page object.

**Serialization fails under Native AOT: "JsonTypeInfo metadata ... was not provided".**
Add the type to your `JsonSerializerContext` and chain it with `ConfigureHttpJsonOptions` (see [Native AOT](#native-aot)). Setting `JsonSerializerIsReflectionEnabledByDefault=false` in regular builds finds these early.

**An error says `Stopped the SSR server: it also accepts connections on ...`.**
The managed Node SSR server listens beyond loopback (the `@inertiajs/vite` default host is `0.0.0.0`), so it was stopped. Rebuild with `inertia({ ssr: { host: '127.0.0.1' } })`, or listen on `127.0.0.1` in your entry. `AllowNetworkExposure = true` keeps it running with a warning.

**SSR does not run and there is no error.**
SSR falls back to client rendering on any failure: look for the warnings `Inertia SSR failed (...)` and `Inertia SSR skipped (...)` in the logs; the first includes the server's hint. Check `o.Ssr.Mode`, `Urls`, that the Node server is running (`GET /health`; with `UseNodeProcess()` look for `Inertia.Net.Ssr` log lines), that `BundlePath` points at the built file, that the path is not in `ExcludePaths`, and that the page is under `MaxPageBytes`. Set `Mode = SsrMode.Required` in Development to surface the failure.

**`Inertia SSR skipped (circuit-open)` or `(overloaded)`.**
`circuit-open`: the server failed `CircuitBreaker.FailureThreshold` times in a row and is skipped for `BreakDuration`; the earlier `Inertia SSR failed` warnings say why. `overloaded`: more renders arrived than `Concurrency.MaxActive` plus `MaxQueued`, or one waited longer than `QueueTimeout`. Add servers or instances, or raise the limits if the server has headroom.

**Several managed instances never become healthy.**
Every instance must listen on its own port: the SSR entry has to read `process.env.INERTIA_SSR_PORT` (see [Let the app run the Node server](#let-the-app-run-the-node-server)). A bundle built only by `@inertiajs/vite` listens on its baked-in port.

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
dotnet test --solution Inertia.Net.slnx -c Release --no-build
bash eng/aot-smoke.sh                    # Native AOT publish + protocol smoke test (Linux, clang + zlib)
```

- Warnings are errors; the code is nullable-annotated and the public API is documented.
- Every protocol rule should be backed by a test in `tests/Inertia.Net.Tests` or a conformance case in `tests/Inertia.Net.IntegrationTests` (which runs against Minimal API, MVC and FastEndpoints hosts). The protocol itself is in [docs/protocol.md](docs/protocol.md), including the deliberate deviations from inertia-laravel.
- Changes are listed in [CHANGELOG.md](CHANGELOG.md). Releases are cut by pushing a `v*` tag: the release workflow builds, tests, packs and publishes to NuGet with the `NUGET_API_KEY` secret of the `release` environment. Give that environment required reviewers, so that a pushed tag alone cannot publish.

Licensed under the [MIT license](LICENSE).
