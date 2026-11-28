# Inertia.Net

> Start here. This README covers setup, everyday tasks, and the full server reference. The [docs site](https://inertia-dotnet.temtem.dev) offers the same topics as shorter, searchable guides.

Inertia.Net lets you build React, Vue or Svelte apps with ASP.NET Core. Your pages use your server's routes and controllers, so you do not build a separate frontend API.

Your C# endpoint chooses a page component and passes the component's data as **props** (named values). The first visit returns HTML.
Later visits fetch a page object as JSON: the component name, its props, the URL and the asset version. The client then updates
the screen without a full reload. You keep ASP.NET Core routing, authentication and validation. You do not need a separate API
or a client-side router for these pages.

Inertia.Net **1.9.9** is a server adapter for the **Inertia.js v3** protocol. It targets .NET 10 and .NET 11 and supports Minimal APIs, MVC and FastEndpoints. Minimal API apps support Native AOT (ahead-of-time compilation). FastEndpoints apps support Native AOT when you add the FastEndpoints source generator. MVC itself does not support Native AOT.

<a id="implementation-tasks"></a>

## Choose your path

| Task | Reference |
|---|---|
| Scaffold a new app | [Template commands](#start-a-new-app) for React, Vue and Svelte |
| Add Inertia to an existing project | [Install the NuGet packages](#installation), then choose [Minimal API](#minimal-api), [MVC](#mvc) or [FastEndpoints](#fastendpoints) |
| Wire up the frontend | [Vite configuration](#vite-config), [React](#react), [Vue](#vue), [Svelte](#svelte) and the [root template](#the-root-template) |
| Add a page or form to a scaffolded app | [Shared C# handlers](src/Inertia.Net.Templates/content/common/Program.cs) and the matching Todos page in [React](src/Inertia.Net.Templates/content/react/ClientApp/pages/Todos/Index.tsx), [Vue](src/Inertia.Net.Templates/content/vue/ClientApp/pages/Todos/Index.vue) or [Svelte](src/Inertia.Net.Templates/content/svelte/ClientApp/pages/Todos/Index.svelte) |
| Follow a complete sample | [Minimal API + React](samples/MinimalApi.React/README.md), [MVC + Vue](samples/Mvc.Vue/README.md) or [FastEndpoints + Svelte](samples/FastEndpoints.Svelte/README.md) |
| Generate contracts, inspect a visit or test a form | [TypeScript declarations](#typescript-types), [typed routes](#typed-routes), [DevTools](#devtools) and [testing](#testing) |
| Check adapter-specific behavior | [Protocol and deliberate deviations](docs/protocol.md) and the [configuration reference](#configuration-reference) |
| Check reference-adapter parity | [Conformance map and framework equivalents](docs/adapter-conformance.md) |
| Work with a coding agent | [For AI agents](#for-ai-agents): files, rules, examples and checks |
| See Inertia in action | The official [Kitchen Sink demo](https://demo-v3.inertiajs.com), with [source](https://github.com/inertiajs/demo-v3) |

### At a glance

| Question | Answer |
|---|---|
| Which Inertia client? | v3: React, Vue or Svelte. v2 is not supported. |
| Which .NET version? | Libraries target .NET 10 and 11. Repository samples and docs run on .NET 10. |
| Which server style? | Minimal APIs, MVC or FastEndpoints. |
| Can I publish Native AOT? | Minimal APIs: yes. FastEndpoints: with its source generator. MVC: no. |
| Where are the examples? | [Local sample apps](#sample-apps), plus the official Laravel/Vue Kitchen Sink above. |
| What changed? | [Changelog](CHANGELOG.md). [Performance results](#performance) are separate from setup. |
| License? | [MIT](LICENSE). |

## First page in five minutes

Install a .NET 10 SDK and Node.js 22.12+ first. In the folder where you want your app, run:

```sh
dotnet new install Inertia.Net.Templates@1.9.9
dotnet new inertia -n MyApp --framework react
cd MyApp
npm install
npm run dev
```

Keep that terminal running. In a second terminal, from `MyApp`, run `dotnet watch`.
Open **http://localhost:5180/todos**, not the Vite address on port 5173. Add a todo with an empty title.
The error message comes from C# validation and appears without a full page reload.

Use `--framework vue` or `--framework svelte` for another client. For .NET 11, install the .NET 11 SDK and add `--tfm net11.0`.
For an existing project, follow [Installation](#installation), [Quick start](#quick-start), and [Client setup](#client-setup).

**Check your setup:** the HTML of the first visit should contain `<script data-page="app" type="application/json">`.
A later visit through a link should return JSON with the header `X-Inertia: true`. If either check fails, check the
[root template](#the-root-template), [client setup](#client-setup), and [troubleshooting](#troubleshooting-and-faq).

## How do I…?

| Task | Guide |
|---|---|
| Share the signed-in user | [Shared data](#shared-data) |
| Submit a form and show errors | [Validation](#validation-and-error-bags) |
| Show a one-time success message | [Flash data](#flash-data) |
| Load a slow query after the page opens | [Deferred props](#deferred-props) |
| Refresh only part of the page | [Partial reloads](#partial-reloads) |
| Append results without duplicates | [Merging props](#merging-props) |
| Reuse a value across visits | [Once props](#once-props) |
| Add numbered pages or a scrolling feed | [Pagination](#pagination), [infinite scroll](#infinite-scroll) |
| Push a live update | [Live props](#live-props) |
| Render HTML on the server | [SSR](#server-side-rendering) |
| Test a page | [Testing](#testing) |
| Handle a missing page or server error | [Error pages](#error-pages) |
| Add a CSP nonce or deploy behind a proxy | [CSP](#content-security-policy), [proxy setup](#deploying-behind-a-proxy) |
| Run several apps or publish AOT | [Mounts](#multiple-apps-and-libraries), [Native AOT](#native-aot) |

## For AI agents

To **build an app**:

- Start with the [template](#start-a-new-app).
- Choose one server style (Minimal API, MVC or FastEndpoints) and one client (React, Vue or Svelte).
- Follow a matching [sample](#sample-apps).
- Use Inertia `Link`, `router` and `useForm`.
- Pass page data through props. Do not add a second JSON API for page data.
- The docs also have [plain-text indexes](docs/Inertia.Net.Docs/wwwroot/llms.txt) and
  [full text](docs/Inertia.Net.Docs/wwwroot/llms-full.txt).

To **change this adapter**, read these in order:

1. [AGENTS.md](AGENTS.md)
2. [docs/protocol.md](docs/protocol.md)
3. The relevant section of this README

Repository rules take precedence. The protocol reference records intentional differences from Laravel. Do not silently remove them.

| Change | Start here | Regression tests |
|---|---|---|
| Page object or prop resolution | `src/Inertia.Net/InertiaPageWriter.cs`, `PropsWriter.cs`, `InertiaProp.cs` | `PageWriterTests`, `PropsResolverTests`, `ResponseTests`, `AdapterParityTests` |
| Headers, versions or redirects | `src/Inertia.Net/InertiaMiddleware.cs`, `InertiaBackResult.cs` | `ProtocolConformanceTests`, `MiddlewareCompositionTests` |
| Shared data, errors or flash | `InertiaOptions.cs`, `InertiaFeature.cs`, `ErrorsResolver.cs`, `InertiaStateStore.cs` under `src/Inertia.Net/` | `PageWriterTests`, `StateStoreTests`, `ValidationNamingTests`, `AdapterParityTests` |
| SSR | `src/Inertia.Net/SsrGateway.cs`, `SsrNodeProcess.cs`, `SsrEndpoint.cs` | `SsrGatewayTests`, `SsrResilienceTests`, `SsrNodeProcessTests` |
| Vite or root HTML | `src/Inertia.Net/ViteAssets.cs`, `RootTemplateView.cs` | `ViteAssetsTests`, `RootTemplateTests`, `MvcTests` |
| DevTools | `src/Inertia.Net/InertiaDevTools.cs` | `DevToolsTests`, `DevToolsRecorderTests` |
| Testing helpers | `src/Inertia.Net.Testing/` | `tests/Inertia.Net.Testing.Tests/` |
| Documentation | `README.md`, `docs/Inertia.Net.Docs/content/docs/` | Docs client build; check examples and links |

Unit tests are in `tests/Inertia.Net.Tests/`. HTTP tests are in `tests/Inertia.Net.IntegrationTests/`.
The shared conformance suite runs against Minimal API, MVC and FastEndpoints hosts.

**Keep these rules:**

- Build HTML and JSON from one page-object path.
- Filter before running loaders.
- Preserve the existing `Vary` header.
- Encode empty errors as `{}`.
- Escape embedded JSON so that it cannot close its script element.
- Never append a second page script after SSR.
- Keep user JSON contracts source-generated for AOT.
- A page may explicitly override `errors`. Do not turn that into a forced merge.
- `OnCompleted` is not status-gated in ASP.NET Core. Do not copy Laravel-specific callback suppression into this adapter.

**Before finishing**, run from the repository root:

```sh
dotnet build Inertia.Net.slnx -c Release -warnaserror
dotnet test --solution Inertia.Net.slnx -c Release --no-build
```

Some changes need more checks:

- For serialization or AOT changes, also run `bash eng/aot-smoke.sh`.
- For client changes, follow [samples/README.md](samples/README.md) and `.github/workflows/ci.yml`.
- Redis and MongoDB tests need their service URLs.
- For docs changes, run `bun install --frozen-lockfile`, then `bun run build`, from `docs/Inertia.Net.Docs`.

When you report:

- Report actual results and skipped checks.
- Never invent a passing result.
- Never add assistant attribution.

When you add protocol behavior:

- Check the upstream specification.
- Update `docs/protocol.md`.
- Reuse the existing writer and helpers.
- Add unit tests and conformance tests.
- Document the public API.
- Update `CHANGELOG.md`.
- Do not change versions unless a release is requested.

## Contents

- [How it works](#how-it-works)
- [Why Inertia instead of a SPA plus a Web API?](#why-inertia-instead-of-a-spa-plus-a-web-api)
- [Start a new app](#start-a-new-app)
- [Sample apps](#sample-apps)
- [Documentation site](#documentation-site)

1. [Overview and feature matrix](#overview-and-feature-matrix)
2. [Installation](#installation) and [.NET 10 and .NET 11](#net-10-and-net-11)
3. [Quick start](#quick-start): [Minimal API](#minimal-api), [MVC](#mvc), [FastEndpoints](#fastendpoints)
4. [Client setup](#client-setup): [Vite config](#vite-config), [React](#react), [Vue](#vue), [Svelte](#svelte)
5. [The root template](#the-root-template)
6. [Props reference](#props-reference), [parallel props](#parallel-props), [scoped services](#scoped-services-in-loaders) and [cached props](#cached-props) ([Redis](#redis))
7. [Shared data](#shared-data)
8. [Partial reloads](#partial-reloads)
9. [Deferred props](#deferred-props)
10. [Merging props](#merging-props)
11. [Once props](#once-props)
12. [Infinite scroll](#infinite-scroll)
13. [Pagination](#pagination) ([EF Core](#with-ef-core), [MongoDB](#with-mongodb))
14. [Live props](#live-props) and [reloading scroll and merge props](#reloading-scroll-and-merge-props)
15. [Validation and error bags](#validation-and-error-bags) and [Precognition](#precognition-live-validation)
16. [Flash data](#flash-data)
17. [Redirects, Location and Back](#redirects-location-and-back), [Error pages](#error-pages), [Authentication and rate limiting](#authentication-and-rate-limiting)
18. [Head tags](#head-tags) and [history encryption, clearing and fragments](#history-encryption-clearing-and-fragments)
19. [Asset versioning](#asset-versioning)
20. [Server-side rendering](#server-side-rendering): [failures](#when-the-server-fails), [several servers](#several-ssr-servers), [observability](#5-observability), [response cache](#6-response-cache)
21. [Vite integration](#vite-integration)
22. [CSRF and antiforgery](#csrf-and-antiforgery)
23. [Deploying behind a proxy](#deploying-behind-a-proxy) and [multiple apps and libraries](#multiple-apps-and-libraries)
24. [Native AOT](#native-aot)
25. [TypeScript types](#typescript-types) ([page records](#page-records), [routes](#typed-routes), [build-time generation](#generating-at-build-time), [compile-time checks](#compile-time-component-checks)), [DevTools](#devtools) and [development checks](#development-checks)
26. [Testing](#testing)
27. [Performance](#performance)
28. [Configuration reference](#configuration-reference)
29. [Migrating from other .NET adapters](#migrating-from-other-net-adapters)
30. [Troubleshooting and FAQ](#troubleshooting-and-faq)
31. [Contributing and license](#contributing-and-license)

---

## How it works

Your .NET endpoint chooses a frontend page and supplies its data as **props**. Inertia connects that endpoint to your React, Vue or Svelte component.

1. The first visit returns an HTML document with the page's data.
2. Later Inertia link clicks and form submissions request the page data in the background.
3. The client updates the page without a full browser reload. Your .NET app still owns routing, authentication and validation.

For example, this endpoint renders a page named `Home` with a `greeting` prop:

```csharp
app.MapGet("/", () => Render("Home", new InertiaProps
{
    ["greeting"] = "Hello from .NET",
}));
```

The matching page receives `greeting` as a component prop. Use the file for your framework under the starter's `ClientApp/pages` directory.

```tsx
// ClientApp/pages/Home.tsx
export default function Home({ greeting }: { greeting: string }) {
  return <h1>{greeting}</h1>
}
```

```vue
<!-- ClientApp/pages/Home.vue -->
<script setup lang="ts">
defineProps<{ greeting: string }>()
</script>

<template>
  <h1>{{ greeting }}</h1>
</template>
```

```svelte
<!-- ClientApp/pages/Home.svelte -->
<script lang="ts">
  let { greeting }: { greeting: string } = $props()
</script>

<h1>{greeting}</h1>
```

The page name must match your client-side page resolver, the function that maps a page name to a component file. The starter templates configure that resolver for you.

### Why Inertia instead of a SPA plus a Web API?

A typical React, Vue or Svelte app on ASP.NET Core has two parts to build: a JSON Web API and a client app that calls it. Each part has its own routing, authentication and validation handling. With Inertia there is one app. Your endpoints render pages, and the frontend is the view layer. You do not build the API layer, the client router or most of the data-fetching code. You keep ASP.NET Core's routing, cookie authentication, validation and dependency injection (DI). Inertia fits apps where one team ships the frontend and the backend together. If you need a public JSON API for mobile apps or third parties, you still build that API separately. The [Why Inertia](https://inertia-dotnet.temtem.dev/docs/why-inertia) page has the full comparison.

## Start a new app

You need the .NET 10 or .NET 11 SDK and JavaScript frontend build tools. npm, Bun, pnpm and Yarn are supported. Choose the package manager you use for your app. Use an Inertia v3 client. Inertia v2 clients are not supported.

Install the template once.

```sh
dotnet new install Inertia.Net.Templates@1.9.9
```

Run the command for your frontend, then change into the new app directory with `cd MyApp`.

| Frontend | Command |
|---|---|
| React | `dotnet new inertia -n MyApp --framework react` |
| Vue | `dotnet new inertia -n MyApp --framework vue` |
| Svelte | `dotnet new inertia -n MyApp --framework svelte` |

Each template uses Minimal APIs and includes working pages, a Todos form, validation, flash messages, CSRF protection and Vite configuration.
The client has `lint` (oxlint), `format` and `format:check` (oxfmt) scripts. Its `build` script type-checks first, with TypeScript 7 for
React and Svelte and TypeScript 6 for Vue. Outside Development the app redirects to HTTPS and sends HSTS.
Every app also generates typed `ClientApp/types/inertia.d.ts` and `routes.ts`, and wires up error pages and DevTools.

Add these options to `dotnet new inertia` when you need them:

- `--tfm net11.0` targets .NET 11. The default target is .NET 10 (LTS).
- `--auth` adds cookie sign-in: a login page with Precognition and a demo user, a protected dashboard, logout, a shared `auth.user`
  and a sign-in rate limit.

In one terminal, from `MyApp`, install the dependencies and start Vite. Use the commands in the row for your package manager:

| Package manager | Install dependencies | Start Vite | Build assets |
|---|---|---|---|
| npm | `npm install` | `npm run dev` | `npm run build` |
| Bun | `bun install` | `bun run dev` | `bun run build` |
| pnpm | `pnpm install` | `pnpm run dev` | `pnpm run build` |
| Yarn | `yarn install` | `yarn run dev` | `yarn run build` |

Start ASP.NET Core in another terminal, also from `MyApp`:

```sh
dotnet watch
```

Open [http://localhost:5180](http://localhost:5180). This is your app. Open your pages through the ASP.NET Core address. Vite supplies the frontend assets and hot reload.

Two servers are running, and only one of them is the app:

| URL | What it is | Open it? |
|---|---|---|
| `http://localhost:5180` | **Your app.** ASP.NET Core handles routing, auth and validation, and renders the Inertia pages. | **Yes** |
| `http://localhost:5173` | The **Vite dev server**. It serves JavaScript, CSS and hot-reload updates to the page above, and has no pages or routes of its own. | No. Opening it shows a blank page or a 404. |

Vite prints `localhost:5173` in large letters when it starts. That is expected. Ignore it and use the ASP.NET Core URL that `dotnet watch` prints.

For a production build, run the build command from the row for your package manager, then `dotnet publish -c Release`. Deploy the built frontend assets with the published app. SSR (server-side rendering) is optional and requires a Node server at runtime. Ordinary client rendering does not require a Node server at runtime.

## Sample apps

[Inertia.Net-Samples](https://github.com/adzamkomladev/Inertia.Net-Samples) has five .NET 10 apps built with Inertia.Net, from basic page rendering to a cache shared across replicas. The three main ones run as live demos:

| App | What it is | Frontend | Database | Source | Live demo |
|---|---|---|---|---|---|
| Inkwell | Multi-user publishing platform with feeds, search, analytics and live notifications | React | SQLite | [samples/Inkwell](https://github.com/adzamkomladev/Inertia.Net-Samples/tree/main/samples/Inkwell) | [inkwell.temtem.dev](https://inkwell.temtem.dev) |
| TechLandier | Electronics storefront with BNPL, warranties, insurance, repairs and rider deliveries | Vue | PostgreSQL | [samples/TechLandier](https://github.com/adzamkomladev/Inertia.Net-Samples/tree/main/samples/TechLandier) | [techlandier.temtem.dev](https://techlandier.temtem.dev) |
| CarParker | Parking-spot rentals with sessions, refunds, spot cover and a guard console | Svelte | MongoDB | [samples/CarParker](https://github.com/adzamkomladev/Inertia.Net-Samples/tree/main/samples/CarParker) | [carparker.temtem.dev](https://carparker.temtem.dev) |

The other two apps, CarParkerReplicas and TechLandierDistributed, run CarParker and TechLandier across replicas. These two apps run locally only. Each sample's README lists its demo logins and how to run it.

This repository's own [`samples`](samples) are the apps that the end-to-end tests run against. The React sample has a `/live` page with live
props over SignalR: a public counter and a shared, private unread count.

## Documentation site

Read the docs at [inertia-dotnet.temtem.dev](https://inertia-dotnet.temtem.dev). The documentation in [`docs/Inertia.Net.Docs`](docs/Inertia.Net.Docs) is itself an Inertia.Net app: a Minimal API renders the pages, React renders the layout, and MDX content provides the guides. It includes search, light/dark/system themes and code examples for every feature. The client uses Bun. To run the site locally, see its [README](docs/Inertia.Net.Docs/README.md).

## Overview and feature matrix

| | Minimal API | MVC controllers | FastEndpoints |
|---|:---:|:---:|:---:|
| Package | `Inertia.Net` | `Inertia.Net` + `Inertia.Net.Mvc` | `Inertia.Net` + `Inertia.Net.FastEndpoints` |
| Render / Back / Location | `Render(...)` returns an `IResult` | same, return `IResult` from an action | `Send.InertiaAsync(...)` or return the result |
| Validation redirect with errors | `.WithInertiaValidation()` | `ModelState` filter (global) | `c.ConfigureInertia()` pre-processor |
| Precognition | `.WithInertiaPrecognition()` | not available | `c.ConfigureInertia()` |
| Razor root view | no (`app.html` template) | optional (tag helpers) | no (`app.html` template) |
| Native AOT | yes | no (MVC itself is not AOT) | yes (with the FastEndpoints source generator) |
| Error pages (routing, authorization, exceptions) | `AddInertiaErrorPages()` | same | same |
| Live props transport: SignalR | `Inertia.Net.Live.SignalR` | same | same |
| Page records (`IInertiaPage`) | `Render(page)` | same | `Send.InertiaAsync(page)` |
| Typed routes (`routes.ts`) | yes | yes | yes |
| Cookie auth redirects, rate-limit rejection | `UseInertiaRedirects()`, `InertiaRateLimiting.OnRejected()` | same | same |
| Named mounts (several apps in one host) | `AddInertia(name, ...)`, `UseInertia(name)` | same | same |

| Client | Package | Supported |
|---|---|:---:|
| React | `@inertiajs/react` v3 | yes |
| Vue | `@inertiajs/vue3` v3 | yes |
| Svelte | `@inertiajs/svelte` v3 | yes |

### Protocol features

- [x] Page object: `component`, `props`, `url`, `version`; JSON responses and the initial `<script data-page="app" type="application/json">` payload
- [x] Asset versioning with a `409` + `X-Inertia-Location` (checked before your handler runs)
- [x] `Vary: X-Inertia`, 302 to 303 for PUT/PATCH/DELETE/QUERY, fragment redirects (`X-Inertia-Redirect`), `Location()`, `Back()`, and
  redirects to another origin turned into `409` + `X-Inertia-Location`
- [x] Partial reloads (`only`, `except`, nested dot paths), `Always`, `Optional`, lazy loaders, opt-in parallel loaders
- [x] Deferred props with groups (`deferredProps`)
- [x] Merge props: append, prepend, deep merge, `matchPropsOn`, reset (`mergeProps`, `prependProps`, `deepMergeProps`)
- [x] Once props with keys, expiry and `fresh` (`onceProps`, `X-Inertia-Except-Once-Props`)
- [x] Infinite scroll (`scrollProps`, merge intent header; `Page<T>` and `CursorPage<T>` from `Pagination`)
- [x] Rescued props (`rescuedProps`)
- [x] Live props (`liveProps`, `X-Socket-Id`, broadcast envelope, `IInertiaLive`), delivered over SignalR by `Inertia.Net.Live.SignalR`
  or by your own broadcaster: see [Live props](#live-props)
- [x] Shared props and `sharedProps`
- [x] Server head tags (`WithHead`, the `head` prop for the client's `serverHead: true`)
- [x] Validation errors, error bags, `WithAllErrors`
- [x] Precognition (live validation)
- [x] Flash data (`flash`)
- [x] `encryptHistory`, `clearHistory`, `preserveFragment`, `preserveBigIntegers`
- [x] Server-side rendering (production Node servers, optionally started and supervised by the app, Vite dev server, per-response modes, circuit breakers, render slots, metrics, health check)
- [x] Vite manifest and dev-server tags, CDN asset base URL, chunk prefetching, CSP nonces
- [x] Inertia DevTools protocol (discovery, correlation headers, entries, read API)

Inertia.Net is not compatible with the Inertia v2 protocol. MVC TempData storage is available through `AddInertiaMvc(o => o.UseTempDataStateStore())`.

---

## Installation

| Package | Install when |
|---|---|
| `Inertia.Net` | always. The core adapter, Minimal API support, Vite, SSR, state store, antiforgery cookie |
| `Inertia.Net.Mvc` | you use MVC controllers: `ModelState` to errors filter, Razor root view and tag helpers |
| `Inertia.Net.FastEndpoints` | you use FastEndpoints: `Send` extensions, validation pre-processor, Precognition |
| `Inertia.Net.Live.SignalR` | you deliver [live props](#live-props) over SignalR: hub, channel authorization, broadcaster |
| `Inertia.Net.EntityFrameworkCore` | you page EF Core queries with numbered or cursor pagination |
| `Inertia.Net.MongoDB` | you page MongoDB finds (`ToPageAsync`, `ToCursorPageAsync`) |
| `Inertia.Net.Redis` | you share cached props and SSR renders across app instances (`AddInertiaRedisCache`) |
| `Inertia.Net.Testing` | in your test project: fluent assertions over Inertia responses |
| `Inertia.Net.Templates` | `dotnet new install Inertia.Net.Templates`: `dotnet new inertia` starts an app (React, Vue or Svelte) |

```sh
dotnet add package Inertia.Net --version 1.9.9
dotnet add package Inertia.Net.Mvc --version 1.9.9 # MVC only
dotnet add package Inertia.Net.FastEndpoints --version 1.9.9 # FastEndpoints only
dotnet add package Inertia.Net.EntityFrameworkCore --version 1.9.9 # EF Core pagination
dotnet add package Inertia.Net.Testing --version 1.9.9 # test projects
dotnet add package Inertia.Net.Live.SignalR --version 1.9.9 # live props over SignalR
dotnet add package Inertia.Net.Redis --version 1.9.9 # Redis cache store
dotnet add package Inertia.Net.MongoDB --version 1.9.9 # MongoDB pagination
```

Inertia.Net requires .NET 10 or .NET 11 and an ASP.NET Core app (`Microsoft.NET.Sdk.Web`). The frontend needs JavaScript build tools with npm, Bun, pnpm or Yarn. Use version `1.9.9` for all Inertia.Net packages. Inertia.Net 1.9.9 uses the Inertia.js v3 protocol. The package version and the protocol version are separate version numbers.

### .NET 10 and .NET 11

Every package ships `net10.0` and `net11.0` assemblies, and NuGet picks the one that matches your app. Things to know on .NET 11:

- **Use 1.8.0 or later on .NET 11.** Inertia.Net 1.7.1 and earlier were built against .NET 10 only. .NET 11 changed the experimental
  resolver API of `Microsoft.Extensions.Validation`. Because of that change, `WithInertiaValidation()` and `WithInertiaPrecognition()`
  from 1.7.1 fail on .NET 11 with `TypeLoadException: Could not load type 'Microsoft.Extensions.Validation.IValidatableInfo'`.
- **Retarget, don't roll forward.** A `net10.0` app that calls `AddValidation()` and runs on the .NET 11 runtime
  (`DOTNET_ROLL_FORWARD=LatestMajor`) fails with any adapter. The validation source generator wrote .NET 10 types that .NET 11
  removed. To fix it, set `<TargetFramework>net11.0</TargetFramework>` and rebuild.
- **Dependencies follow the target framework.** On `net11.0`, `Inertia.Net.EntityFrameworkCore` depends on EF Core 11 and
  `Inertia.Net.FastEndpoints` depends on FastEndpoints 9. On `net10.0` they depend on EF Core 10.0.15 and FastEndpoints 8.3.0.
  Reference the `FastEndpoints.Generator` version that matches your FastEndpoints version.
- **Async validators.** Errors from `AsyncValidationAttribute` and `IAsyncValidatableObject` are handled like synchronous errors:
  `WithInertiaValidation()` redirects back with the errors, and `WithInertiaPrecognition()` returns `422`.
- **Automatic CSRF check.** Apps built with `WebApplication.CreateBuilder` also get the framework's `Sec-Fetch-Site`/`Origin` check.
  `UseInertiaAntiforgery()` is still the token check Inertia needs. See [CSRF and antiforgery](#csrf-and-antiforgery).
- **C# 15 unions** work as props and in the generated TypeScript. See [Props reference](#props-reference) and [TypeScript types](#typescript-types).
- **Compression.** `AddResponseCompression()` also offers Zstandard and always adds `Vary: Accept-Encoding`. Inertia's `Vary: X-Inertia`
  stays next to it.

### Factory name collision
> The namespace is `Inertia.Net` and the static factory class is `Inertia.Net.Inertia`. C# resolves a leading `Inertia` to the *namespace*, so `Inertia.Render(...)` does **not** compile in your app (outside the `Inertia.Net` namespace).
> To avoid this, the package's `buildTransitive` props file adds `global using Inertia.Net;` and `global using static Inertia.Net.Inertia;` to your project when `<ImplicitUsings>enable</ImplicitUsings>` is set (the default in new projects). Write `Render(...)`, `Defer(...)`, `Once(...)`, `Back()`, `Location(...)` without a prefix.
> To opt out, set `<InertiaNetImplicitUsings>false</InertiaNetImplicitUsings>`. Then do one of these: add `using static Inertia.Net.Inertia;` yourself, use an alias such as `using InertiaFactory = global::Inertia.Net.Inertia;`, or call `global::Inertia.Net.Inertia.Render(...)`.
> `HttpContext.Inertia()` gives access to per-request flash, errors and history flags. It is an extension method and works as written.

---

## Quick start

New to Inertia.Net? Follow [Your first 10 minutes](docs/Inertia.Net.Docs/content/docs/first-10-minutes.mdx) for a step-by-step first app.

Already using the starter template? Its services, middleware, root template and frontend are configured for you. The steps below are for adding Inertia to an existing app or building one manually. Choose one backend and one frontend.

For a new project without the starter template:

1. Run `dotnet new web -n MyApp`.
2. Run `cd MyApp`.
3. Install the packages above.
4. Create a `package.json` before the client setup, with `npm init -y`, `bun init -y`, `pnpm init` or `yarn init`.

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

In your `.csproj` file, make sure `app.html` is copied to the output directory and the publish directory:

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

This first page needs no database. Add a `Home` component using the client example for your framework below. The later CRUD examples use application-specific services such as `AppDb`. Replace those services with your own data layer. Form endpoints also need the validation setup in [Validation and error bags](#validation-and-error-bags).

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

Controllers return the core `IResult` factories (`Render`, `Back`, `Location`). MVC executes any `IResult`. `Inertia.Net.Mvc` adds the `ModelState` filter and the optional Razor root view. See [Validation](#validation-and-error-bags).

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
app.UseFastEndpoints(c => c.ConfigureInertia());           // validation redirect + Precognition
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

`Send.InertiaAsync`, `Send.InertiaLocationAsync` and `Send.InertiaBackAsync` are the `Send` extensions. To send errors or flash data with a redirect back, send `Back().WithErrors(...).WithFlash(...)` with `Send.ResultAsync(...)`.

---

## Client setup

Run the client setup commands from the ASP.NET Core project directory. This is the directory that contains your `.csproj` file. Install only the packages in the row for your framework, then the shared development dependencies. Use the commands for your package manager:

| Package manager | Add packages | Add development packages |
|---|---|---|
| npm | `npm install <packages>` | `npm install -D <packages>` |
| Bun | `bun add <packages>` | `bun add -D <packages>` |
| pnpm | `pnpm add <packages>` | `pnpm add -D <packages>` |
| Yarn | `yarn add <packages>` | `yarn add -D <packages>` |

Replace `<packages>` with the package names and version ranges from your framework row.

Inertia.Net uses the v3 protocol. The initial page is the `<script data-page="app" type="application/json">` element, and the v3 clients read that element directly. Install the v3 client packages for your framework:

| Framework | Packages |
|---|---|
| React | `react react-dom @vitejs/plugin-react @inertiajs/react@^3` |
| Vue | `vue @vitejs/plugin-vue @inertiajs/vue3@^3` |
| Svelte | `svelte @sveltejs/vite-plugin-svelte @inertiajs/svelte@^3` |

Add `vite typescript @types/node` as development dependencies using your package manager's command above.
For React, also add `@types/react @types/react-dom`. `@types/node` provides the types for `node:fs` and `process` used by
the Vite config below. The page resolver and `import.meta.glob` types come from `vite/client`, so list that entry in `tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "jsx": "react-jsx",
    "strict": true,
    "noEmit": true,
    "skipLibCheck": true,
    "types": ["vite/client", "node"]
  },
  "include": ["resources/js", "vite.config.ts"]
}
```

For Vue and Svelte, use `"jsx": "preserve"` or omit that option.

The starter templates use `ClientApp/pages`. The manual setup below uses `resources/js/Pages`. Keep the resolver, file paths and Vite entry consistent with the layout you choose.

The starter templates also ship `ClientApp/lib/inertia-signalr.ts`, the client transport for [live props over SignalR](#signalr).
To turn it on, uncomment `live: signalr()` in `ClientApp/app.tsx` (or `app.ts`).

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

Pass the manifest key of the entry (`resources/js/app.tsx`) to `@vite("...")`.

The optional `@inertiajs/vite` plugin can resolve pages and mount the app for you (`createInertiaApp()` with no arguments). The entry files below write out the resolver and the mount code in full, so they work with or without the plugin. Check the [Inertia v3 client docs](https://inertiajs.com/docs/v3/installation/client-side-setup) for the plugin's options.

### React

```tsx
// resources/js/app.tsx
import { createInertiaApp, type ResolvedComponent } from '@inertiajs/react'
import { createRoot } from 'react-dom/client'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob<{ default: ResolvedComponent }>('./Pages/**/*.tsx', { eager: true })
    return pages[`./Pages/${name}.tsx`]
  },
  setup({ el, App, props }) {
    if (!el) throw new Error('Inertia root element not found')
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

The following `Users/Index` page shows a list that your own `/users` endpoint supplies:

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
import { createApp, h, type DefineComponent } from 'vue'
import { createInertiaApp } from '@inertiajs/vue3'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob<{ default: DefineComponent }>('./Pages/**/*.vue', { eager: true })
    return pages[`./Pages/${name}.vue`]
  },
  setup({ el, App, props, plugin }) {
    if (!el) throw new Error('Inertia root element not found')
    createApp({ render: () => h(App, props) }).use(plugin).mount(el)
  },
})
```

Use `@vite("resources/js/app.ts")` in the root template and `input: 'resources/js/app.ts'` in the Vite config. Replace the React plugin with `import vue from '@vitejs/plugin-vue'` and `plugins: [vue(), hotFile()]`. Remove `@viteReactRefresh`.

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
import { createInertiaApp, type ResolvedComponent } from '@inertiajs/svelte'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob<ResolvedComponent>('./Pages/**/*.svelte', { eager: true })
    return pages[`./Pages/${name}.svelte`]
  },
  setup({ el, App, props }) {
    if (!el) throw new Error('Inertia root element not found')
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

Start Vite with `npm run dev`, `bun run dev`, `pnpm run dev` or `yarn run dev` in terminal 1. Vite writes `wwwroot/hot`. In terminal 2:

```sh
dotnet watch         # the app, ASPNETCORE_ENVIRONMENT=Development
```

Open the ASP.NET Core URL printed by `dotnet watch` (for example `http://localhost:5180`), **not** the Vite URL (`http://localhost:5173`). Vite only serves assets and hot-reload updates to the page that ASP.NET Core renders, so the Vite address shows a blank page. The first visit to `/` should display "Hello from .NET". If you started with `dotnet new web`, make sure the launch profile sets `ASPNETCORE_ENVIRONMENT=Development` so that the adapter reads the Vite hot file.

For production, run your package manager's `build` script (see [Start a new app](#start-a-new-app)), then `dotnet publish -c Release`. Deploy `wwwroot/build` and `app.html` with the published app. You need Node.js to build the assets. At runtime you need Node.js only if you enable SSR. Samples for each framework are in [samples/](samples/).

---

## The root template

The root template is the HTML file for the first visit. The default root view parses this file on its first render into literal chunks plus tokens. In Development it reads the file again when the file changes. The first render throws when `@inertia` is missing. `InertiaOptions.RootView` sets the path, relative to the content root. The default is `app.html`.

| Token | Output |
|---|---|
| `@inertia` | Without SSR: `<script data-page="app" type="application/json">{page}</script><div id="app"></div>`. With SSR: the server-rendered body, verbatim (it already contains the script element). **Required.** |
| `@inertiaHead` | The SSR `head` tags joined with `\n`. Without SSR (or when it fails), the server head tags from `Render(...).WithHead(...)`. Otherwise empty. |
| `@vite("resources/js/app.tsx")` | The Vite tags for one or more comma-separated entries: `@vite("a.ts", "b.css")`. See [Vite integration](#vite-integration). |
| `@viteReactRefresh` | The React Fast Refresh preamble while the dev server runs; empty otherwise. Put it before `@vite`. |
| `@viewData("title")` | An HTML-encoded value passed with `Render(...).WithViewData("title", "Dashboard")`. Empty when absent. |
| `@@` | A literal `@`. |

Inertia.Net leaves other `@` characters alone, for example in CSS at-rules and e-mail addresses. Token arguments use single or double quotes.

The page JSON is not HTML-entity encoded. `<`, `>`, `&` and `/` are written as `\uXXXX` escapes, so `</script>` in the page JSON can never close the script element. This holds whatever your `JavaScriptEncoder` setting is.

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

The name `"App"` finds `Views/Shared/App.cshtml`. You can also pass an app-relative path such as `"~/Views/App.cshtml"`. The view receives an `InertiaRootViewContext` model:

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

`<vite entry="a.ts, b.css" />` takes comma-separated entries. SSR runs at most once per request, and `<inertia-head />` and `<inertia />` share that one render. Data from `WithViewData` is in `ViewData`. `WithRootView("Other")` takes Razor view names when the Razor root view is on.

### A custom root view

To write the first-visit HTML yourself, implement `IInertiaRootView`. Your implementation receives the buffered page JSON and writes the response. Register it before `AddInertia`, which uses `TryAdd`:

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

Props are the named values that an endpoint passes to its page component. `Render(component, props)` takes the props in one of these forms:

- an `InertiaProps` (an ordered `Dictionary<string, object?>`)
- any `IDictionary<string, object?>`
- a typed object whose properties are the props

You can also pass an `IInertiaPage` record that names its own component: `Render(new UsersPage(users))` (see [Page records](#page-records)). Inertia.Net walks nested `InertiaProps` and dictionaries, so you can nest props at any depth.

Every page has an `errors` prop. It is an empty object when there are no errors. The key order in `props` is `errors`, then the shared props, then the page props.

**C# 15 unions (.NET 11).** A prop whose type is a `union` (for example `public union Subject(User, Team);`) is written as the JSON of its
active case, with no wrapper or discriminator. Serialization goes through your source-generated `JsonSerializerContext`. Partial reloads
select a union prop like any other prop. An `InertiaProp` (`Defer`, `Optional`, ...) stored inside a union case is not resolved. Keep lazy
props at the page level.

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
| `Scroll(page, wrapper: "data", metadata)` | An infinite-scroll prop: merged at `{path}.{wrapper}` plus paging metadata. `Page<T>` and `CursorPage<T>` supply metadata and use `data` as their wrapper. |
| `Live(() => ..., LiveEvent)` | Reloaded (or updated in place) when the event is broadcast. Chain `.Live(...)` on any non-merge prop, `.Throttle(window)`. See [Live props](#live-props). |

Every lazy factory (`Prop`, `Always`, `Optional`, `Defer`, `Merge`, `DeepMerge`, `Once`, `Scroll`, `Live`) accepts five loader shapes:

```csharp
Prop(() => value)                                  // Func<T>
Prop(() => repo.GetAsync())                        // Func<Task<T>>
Prop(() => repo.GetValueTaskAsync())               // Func<ValueTask<T>>
Prop(ct => repo.GetAsync(ct))                      // Func<CancellationToken, Task<T>>
Prop(ct => repo.GetValueTaskAsync(ct))             // Func<CancellationToken, ValueTask<T>>
```

The cancellation token is `HttpContext.RequestAborted`. The overloads bind so that an `async` lambda always selects the overload that awaits it. An `async` lambda never becomes "a prop whose value is a Task".

Fluent methods change the prop instance they are called on and return that same instance. Create a separate prop for each use. Do not share prop instances across requests. `.Fresh(false)` clears freshness and keeps the once behavior. `.WithGroup(name)` delegates to `.Defer(name)`. Repeated `.MatchOn(...)` calls accumulate keys. Root append and root prepend are mutually exclusive. The fluent methods are: `.Once(key?, until?)`, `.As(key)`, `.Until(ttl)`, `.Fresh()`, `.Append(path?, matchOn?)`, `.Prepend(path?, matchOn?)`, `.DeepMerge()`, `.MatchOn(params keys)`, `.Defer(group)`, `.WithGroup(group)`, `.Rescue()`, `.Parallel()`, `.Cache(key, duration)`, `.Cache(key, duration, staleFor?, tags?)`, `.Live(event)`, `.Live(channel, events)`, `.Throttle(window)`.

**Loaders run sequentially** by default, in prop order, and only when their prop is included in the response. That makes a scoped EF Core `DbContext` safe to use in several loaders. Mark independent async loaders with [`.Parallel()`](#parallel-props) to start them together. A loader that returns another prop (for example `Prop(() => Defer(...))`) is unwrapped once.

Inertia.Net buffers the page before sending it, so a loader that throws never produces a partly written 200 response.

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

- Only loaders that return `Task` or `ValueTask` can be parallel. `Parallel()` throws for values and synchronous loaders.
- At most `InertiaOptions.Props.MaxConcurrency` loaders run at once per render. The default is 4. To change it: `builder.Services.AddInertia(o => o.Props.MaxConcurrency = 8);`
- Top-level props and props inside literal objects (`InertiaProps` and other string-keyed dictionaries) start early. Parallel props anywhere else resolve in turn.
- Loaders of props that aren't part of the response (partial reloads, deferred or optional props on the first visit) never start.
- Pages without parallel props skip the work of finding them. A component remembers whether its renders have parallel props. If it was first rendered without them and later gets some, that one render resolves them in turn, and every later render starts them early.
- If a loader fails, the others are cancelled and awaited, and the render fails with the first failure in prop order. Props marked with `Rescue()` fail alone, as usual.

**DbContext and HttpContext:** a parallel loader must not use the request's `DbContext` or other scoped services that are not thread-safe. A parallel loader must not touch `HttpContext`. EF Core throws "A second operation was started on this context instance…". Give each loader its own context through `IDbContextFactory<T>` or its own scope:

```csharp
["orders"] = Prop(async ct =>
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);
    return await db.Orders.CountAsync(ct);
}).Parallel(),
```

### Scoped services in loaders

`Prop<TService, T>(...)` passes a service from the request to a loader, so the loader does not capture the service in a closure:

```csharp
["orders"] = Prop<AppDbContext, int>((db, ct) => db.Orders.CountAsync(ct)),
["inbox"] = Prop<InboxService, Inbox>((inbox, ct) => inbox.LoadAsync(ct)).Defer(), // and .Once(), .Cache(...), .Live(...), .Rescue()
```

- A loader that runs during the request gets the request's instance (the same `DbContext` the handler used).
- A `.Parallel()` loader gets a scope of its own, so two parallel loaders never share a `DbContext`. The scope is disposed when the
  loader finishes.
- A [stale-while-revalidate](#cached-props) refresh runs after the response, in a new scope. That scope is disposed when the refresh ends.
- If the service is not registered, the render fails with the prop's path and the service type.

### Cached props

`.Cache(key, duration)` keeps a prop's value as JSON in the cache. The default cache is the app's `IMemoryCache`. While the entry lives,
the loader does not run and the stored JSON is written as it is. A cache hit therefore costs neither the query nor the serialization:

```csharp
return Render("Dashboard", new InertiaProps
{
    ["stats"] = Prop(ct => stats.ComputeAsync(ct)).Cache("dashboard-stats", TimeSpan.FromMinutes(5)),
    ["orders"] = Defer(ct => orders.RecentAsync(userId, ct)).Cache($"orders:{userId}", TimeSpan.FromMinutes(1), tags: [$"user:{userId}"]),
    ["catalog"] = Optional(() => catalog.All()).Cache("catalog", TimeSpan.FromHours(1), staleFor: TimeSpan.FromHours(23)),
});
```

- **The key is the value's identity.** Every request that uses a key gets the same value. Put the user, the tenant and anything else
  the value depends on in the key. Keys live in their own space in the cache, so they cannot collide with the app's own entries.
- **Only loading changes.** Optional, deferred, merged, once and scroll props keep their behavior. Their metadata is written from the
  cache too. A parallel cached prop does not start its loader on a hit. A rescued failure is not cached.
- **Plain values only.** The value must not contain `InertiaProp` values. A loader that returns a prop cannot be cached.
- **Per instance.** With the default store each app instance has its own memory cache. Concurrent misses share one fill per key and
  app instance. If that fill fails or is cancelled, a waiting request retries with its own loader. Entries carry a `Size` (their byte
  length), so an `IMemoryCache` with a `SizeLimit` works.

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

`ClearPropsAsync()` removes every cached prop. `ClearSsrAsync()` removes every [cached render](#6-response-cache). `SetPropAsync`
serializes the value as a prop of its type would be, and takes the same `staleFor` and `tags`. A scroll value's paging metadata comes
from `IProvidesScrollMetadata`. With the default store a removal frees the entries at once.

A removal or a `SetPropAsync` call waits for in-flight writes to the affected key or tag on this app instance. A load or refresh of that
key (or of a prop with that tag) that started before the removal or the `SetPropAsync` call neither stores nor shares its value
afterwards. Later requests see the new value, or load again. Loads of other keys and tags are not affected. This guarantee is per app
instance. With a shared store (Redis, MongoDB), a load on another instance that finishes after the removal can still store a value that
it read before the removal.

The default store keeps cached props in the app's `IMemoryCache`, with no size limit of their own. To cap them, set
`o.Props.CacheMaxBytes`. Cached props then go in a private cache, and the least recently used are evicted past the cap.

**Stale-while-revalidate.** `staleFor` keeps serving a value after its `duration` has passed. The first request that reads a stale
value gets the stale value at once. The loader runs after that response has been sent, and stores a fresh value. Requests in the
meantime get the stale value too. After `duration + staleFor`, the loader runs during the request again, as it does without `staleFor`.

- When the response completes, the refresh is queued on a bounded background worker, so an HTTP/1.1 connection can take its next request
  immediately. Loaders using `staleFor` must use singleton dependencies or create an independent scope inside the loader (for example
  with `IServiceScopeFactory.CreateAsyncScope()` or `IDbContextFactory<T>`). Do not capture request-scoped services or `HttpContext`.
- Four refreshes run at a time. Each refresh is cancelled after 30 seconds, and a loader that ignores the cancellation token is
  abandoned. The worker accepts 64 pending refreshes and stops with the host. When the queue is full, the worker drops the refresh,
  releases its claim and logs a warning (at most every 30 seconds, with a count). The stale value keeps its existing expiration.
  Monitor `inertia.cache.refresh.dropped` with `inertia.cache.kind=prop|ssr`.
- One refresh runs per key and app instance at a time, and the stale props of one response refresh one after another. A refresh skips
  its loader when it finds the value fresh again. That happens when another refresh, or another app instance sharing the store, has
  stored a value since.
- A refresh that fails is logged as a warning and keeps the stale value. The next stale read tries again.

**Another store.** Props and [SSR renders](#6-response-cache) are kept in an `IInertiaCacheStore`. To keep them elsewhere, register your
own store as a singleton, before or after `AddInertia()`. For example, you can keep them in a cache that every app instance shares:

```csharp
builder.Services.AddSingleton<IInertiaCacheStore, MyCacheStore>();
```

The store gets and sets byte arrays by `InertiaCacheKey`, and removes by key and by tag. An `InertiaCacheKey` is a kind and a key.
For stores keyed by strings, `ToString()` gives `inertia:prop:{key}` or `inertia:ssr:{hash}`. The values carry their own fresh and
expiry times, so a store only keeps each one for `InertiaCacheEntryOptions.Expiration`. Every entry also carries the tag
`InertiaCacheTags.Props` or `InertiaCacheTags.Ssr`, which the clear methods remove. While a page renders, a store that throws is
logged (at most once every 30 seconds) and counts as a miss. `IInertiaCache` lets the exception through. A value in a format that
Inertia.Net does not know reads as a miss. Prop JSON outlives a deploy in a shared store. Put a version in keys whose shape changes,
or clear them on deploy.

### Redis

`Inertia.Net.Redis` keeps cached props and SSR renders in Redis, so every app instance shares them. A `RemoveByTagAsync` call on one
instance clears them for all instances. A page's cached props are read from Redis together, not one round trip after another.
Their loaders still run in turn unless you mark them `Parallel()`:

```csharp
builder.Services.AddInertiaRedisCache(o =>
{
    o.Configuration = "localhost:6379";   // or leave it null to use a registered IConnectionMultiplexer
    o.KeyPrefix = "myapp:";               // default inertia.net:; on Redis Cluster use a hash tag such as {myapp}:
});
```

Storing an entry is a single script, and so is removing an entry, so an entry and its tag sets never disagree. Removing a tag works
in batches, so a tag on every entry does not block the server. When the Redis server cannot be reached, renders run their loaders
(the failure is logged and counts as a miss) and `IInertiaCache` removals throw. See the [package README](src/Inertia.Net.Redis/README.md).

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

`expiresAt` is milliseconds since the Unix epoch, or `null` without `.Until(...)`. When the client already holds the key, it sends `X-Inertia-Except-Once-Props`. The response then leaves the value out and keeps the `onceProps` entry.

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

**Live**

```csharp
["order"] = Prop(ct => orders.GetAsync(id, ct)).Live(OrderEvents.Updated(id)).Throttle(TimeSpan.FromMilliseconds(500)),
```

```json
{
  "props": { "errors": {}, "order": { "id": 1, "status": "paid" } },
  "liveProps": {
    "order": {
      "listeners": [ { "channel": { "name": "orders.1", "type": "private" }, "events": ["OrderUpdated"] } ],
      "throttle": 500
    }
  }
}
```

**Typed page props**

A typed object works with Native AOT. Inertia.Net walks the object through its `JsonTypeInfo`. It resolves members of type `InertiaProp` and serializes the other members normally. Property names follow the JSON naming policy. The default policy is camelCase.

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

Register `UsersPage` and its value types in your `JsonSerializerContext` (see [Native AOT](#native-aot)). Anonymous types (`new { users }`) work only when reflection-based serialization is enabled, so they do not work under AOT.

**Dot keys.** A top-level key containing dots is unpacked: `["user.name"] = "Ann"` becomes `"user": { "name": "Ann" }`.

**BigInteger safety.** With `o.PreserveBigIntegers = true`, integers beyond ±(2^53 − 1) are written as `{"$bigint":"9007199254740993"}` and the page gets `"preserveBigIntegers": true`.
The default is false. Use the handler fragment `Render("Orders/Show", props).PreserveBigIntegers()` to opt in for one response,
or `.PreserveBigIntegers(false)` to opt out. Both props and flash follow that response's setting.

---

## Shared data

Shared props are props that Inertia.Net merges into every page. Use them for values that every page needs. When a page prop has the same key as a shared prop, the page prop wins.

Register shared props in the options of `AddInertia`:

```csharp
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Demo");                                                    // static value
    o.Share("auth", ctx => new AuthInfo(ctx.User.Identity?.Name));                // per request: Func<HttpContext, T>, typed in the generated TypeScript
});
```

You can also share a value for the current request only. Do this from any code that has an `HttpContext`, such as middleware or a handler:

```csharp
app.Use((context, next) =>
{
    context.Inertia().Share("locale", CultureInfo.CurrentUICulture.Name);
    return next(context);
});
```

A shared value can be any kind of prop. For example, `o.Share("permissions", ctx => Once(() => LoadPermissions(ctx)))` works too.
`o.Share("plans", Once(() => plans.All()))` is the equivalent of Laravel's `shareOnce`: the client keeps the value across visits. The page response lists the top-level shared keys, as in `"sharedProps": ["errors", "appName", "auth"]`, so that the client can keep them across navigations. To turn that list off, set `o.ExposeSharedPropKeys = false`.

---

## Partial reloads

A partial reload asks the server for some of a page's props instead of all of them. Use it to refresh part of a page without loading the other props again. The client sends the `X-Inertia-Partial-Component` header, together with `X-Inertia-Partial-Data` (the `only` list) or `X-Inertia-Partial-Except` (the `except` list). The server then filters the props by these rules:

- The server filters props only when the partial component is the same as the component it renders.
- Paths use dots to reach nested props, and match on a `.` boundary. `only: ["user"]` includes `user.name`. `only: ["user.name"]` includes `user.name` and keeps `user` as a container.
- `except` takes priority over `only`. A prop made with `Always(...)` ignores both lists.
- A loader is the function that produces the value of a prop. The server does not filter the children of a value that a loader produces. It does filter nested literal `InertiaProps`.
- The loader of an excluded prop never runs.
- When `only` selects a path inside an array, such as `rows.1.extra`, the server reloads the complete array, including sibling fields and nested loaders. When `except` names a path inside an array, the server excludes the entire array. `Always` still wins. To update items independently, use objects keyed by ID instead of an array. Direct array elements that are omitted or rescued keep their positions as `null`.
- A missing or empty list header means "no filter", which differs from an empty list.

For example, the client asks for one prop:

```tsx
router.reload({ only: ['users'] })
```

On the server, only the loader of that prop runs:

```csharp
["users"] = Prop(ct => db.Users.ToListAsync(ct)),    // runs on the partial reload above
["roles"] = Prop(ct => db.Roles.ToListAsync(ct)),    // does not run
```

---

## Deferred props

A deferred prop loads after the page first renders. Use it for a value that should not delay the first response. `Defer` leaves the prop out of the first response and lists the prop under `deferredProps`. As soon as the page is mounted, the client requests each group of deferred props with a partial reload.

```csharp
return Render("Dashboard", new InertiaProps
{
    ["user"] = user,
    ["stats"] = Defer(ct => reports.StatsAsync(ct)),
    ["chart"] = Defer(ct => reports.ChartAsync(ct), group: "charts"),
    ["activity"] = Defer(ct => reports.ActivityAsync(ct), group: "charts"),
});
```

Deferred props in the same group load in one request. Different groups load in parallel requests. The server only produces `deferredProps` on renders that are not partial reloads. You can combine a deferred prop with `Merge`, `Once` or `.Rescue()`.

On the client, show a fallback until the deferred prop arrives:

```tsx
import { Deferred } from '@inertiajs/react'

<Deferred data="stats" fallback={<Spinner />}><Stats /></Deferred>
```

---

## Merging props

By default, a reloaded prop replaces the value that the client holds. A merge prop tells the client to combine the new value with the value that it holds. The table shows each way to merge.

| C# | Metadata | Client behavior |
|---|---|---|
| `Merge(v)` | `mergeProps: ["key"]` | append to the existing array |
| `Prop(...).Prepend()` | `prependProps: ["key"]` | prepend |
| `Merge(v).Append("path")` | `mergeProps: ["key.path"]` | append at a nested path |
| `Prop(...).Prepend("path")` | `prependProps: ["key.path"]` | prepend at a nested path |
| `DeepMerge(v)` | `deepMergeProps: ["key"]` | deep merge objects and arrays |
| `.MatchOn("id")` | `matchPropsOn: ["key.id"]` | replace items that have the same `id` instead of duplicating them |
| `.Append("path", matchOn: "id")` | `matchPropsOn: ["key.path.id"]` | the match key is relative to the nested path |

The server leaves out the merge metadata for paths listed in the `X-Inertia-Reset` header, because the client wants a fresh start. On a partial reload, the server also leaves out the merge metadata for paths that the reload did not ask for.

```csharp
["posts"] = Merge(ct => db.Posts.Page(page).ToListAsync(ct)).MatchOn("id"),
```

---

## Once props

Use a once prop for an expensive value that rarely changes. The client remembers a once prop across visits. The client tells the server which keys it holds, in the `X-Inertia-Except-Once-Props` header.

```csharp
["countries"] = Once(ct => db.CountriesAsync(ct)),                                  // key = the prop path
["plans"]     = Once(ct => db.PlansAsync(ct)).As("billing-plans"),                   // custom key
["rates"]     = Once(ct => fx.RatesAsync(ct)).Until(TimeSpan.FromMinutes(10)),       // client expires it
["tax"]       = Once(ct => db.TaxAsync(ct)).Fresh(),                                 // always re-send
["flags"]     = Prop(ct => db.FlagsAsync(ct)).Once(key: "flags", until: TimeSpan.FromHours(1)),
```

- On later visits, the server leaves the value out. The `onceProps` entry stays, so the client keeps its copy.
- `.Fresh()` sends the value even when the client has it.
- A partial reload always resolves the once props that it asks for.
- Inertia.Net computes expiry from the registered `TimeProvider`, so tests can use `FakeTimeProvider`.
- `.As(key)` and `.Until(ttl)` each make the prop a once prop. When several pages use the same key, the client reuses one value across those pages.

---

## Infinite scroll

Infinite scroll loads more items as the user scrolls and adds them to the list that is already on the page. Use `Scroll(...)` for a list that grows this way. `Scroll(...)` merges the items inside the value and reports paging metadata to the `<InfiniteScroll>` client component. The key that holds the items inside the value is called the *wrapper*. The default wrapper is `"data"`.

```csharp
app.MapGet("/feed", (PageRequest request) => Render("Feed", new InertiaProps
{
    ["posts"] = Scroll(Pagination.Paginate(AppState.Posts, request)),
}));
```

- `ScrollMetadata(PageName, PreviousPage, NextPage, CurrentPage)` is a record. Page values can be `int`, `long`, `string` or `null`. Cursor paging uses strings.
- The paging metadata comes from the `metadata:` argument. Without that argument, it comes from the value, if the value implements `IProvidesScrollMetadata`. When neither is available, the render throws an exception with a message that names the prop.
- When the user scrolls up, the client sends `X-Inertia-Infinite-Scroll-Merge-Intent: prepend`. The server then reports the wrapper under `prependProps` instead of `mergeProps`.
- `scrollProps[path].reset` is true when the request lists the path in `X-Inertia-Reset`.
- `Scroll(...)` accepts the five loader shapes. You can combine it with `.Defer()`, `.Once()` and `.Rescue()`. A deferred scroll prop reports `"posts.data"`, the same as a resolved scroll prop. This is a deliberate fix of an inertia-laravel quirk.

On the client, render the items inside the component:

```tsx
import { InfiniteScroll } from '@inertiajs/react'

<InfiniteScroll data="posts">{posts.data.map((p) => <Post key={p.id} post={p} />)}</InfiniteScroll>
```

---

## Pagination

`Pagination` creates page values, for a classic pager or for infinite scroll. `Scroll` adds infinite-scroll metadata and merging to a page value. For a classic pager, use the page value directly, without `Scroll`.

| You write | You get |
|---|---|
| `Pagination.Paginate(posts, request)` | A numbered `Page<T>` for a pager |
| `Scroll(Pagination.Paginate(posts, request))` | Numbered infinite scroll |
| `Scroll(await Pagination.PaginateAsync(request, key, fetch, ct: ct))` | Cursor-based infinite scroll using your data source |
| `Scroll(ct => Pagination.PaginateAsync(request, fetch, ct: ct)).Defer()` | A deferred page. Other prop modifiers also work |

A Minimal API handler can take `PageRequest` or `CursorRequest` directly as a parameter, as the example in the Infinite scroll section shows. In MVC and FastEndpoints, call `PageRequest.From(Request.Query)` or `CursorRequest.From(HttpContext)`. A page number below 1 becomes 1. An invalid page size uses the default, and a size above the maximum is clamped to the maximum. Set the defaults and the parameter names with `o.Pagination` in `AddInertia`. `From` and `BindAsync` keep the query parameter name, and the returned page uses that name. An explicit paginator name overrides it.

`Pagination.Paginate` slices an `IReadOnlyList<T>` that is already in memory. For a database, search SDK or HTTP API, use `Pagination.PaginateAsync`. It accepts a `fetch(skip, take, ct)` delegate and an optional count delegate. `Pagination.PaginateAsync` requests one extra item to work out `hasMore`. Without a count delegate, `total` and `lastPage` are null. The count is skipped when the page already gives the total: a first page with no more items, or a last page with items. When the delegate offset is above `int.MaxValue`, the result is an empty page and `fetch` is not called, but the optional count still runs. Give your data source a stable, fully unique ordering. For a classic pager, build the links from `currentPage`, `lastPage`, and `pageName`, because a page does not contain URL links.

A numbered page looks like this as JSON:

```json
{ "data": [ { "id": 21 } ], "currentPage": 2, "perPage": 20, "total": 95, "lastPage": 5, "from": 21, "to": 40, "hasMore": true, "pageName": "page" }
```

`from` and `to` are one-based. Both are null on an empty page. The examples shorten the data array.

For example, a data source can implement numbered pages without exposing `IQueryable`:

```csharp
var page = await Pagination.PaginateAsync(
    request,
    (skip, take, ct) => store.FetchPostsAsync(skip, take, ct),
    ct => store.CountPostsAsync(ct),
    ct: cancellationToken);
```

For a React pager, use Inertia links and preserve any other filters your app needs:

```tsx
import { Link } from '@inertiajs/react'

{page.lastPage != null && Array.from({ length: page.lastPage }, (_, i) => i + 1).map((n) => (
  <Link key={n} href={`?${page.pageName}=${n}`} aria-current={n === page.currentPage ? 'page' : undefined}>
    {n}
  </Link>
))}
```

### Cursors

A cursor page is addressed by a cursor instead of a page number. Passed to `Scroll`, a cursor page gives cursor-based infinite scroll. The cursor overload of `PaginateAsync` takes a key selector and a fetch delegate. The fetch delegate receives three arguments:

- a decoded cursor with `Direction` (`After` or `Before`) and `Value`
- a take count
- a cancellation token

Return rows in the requested direction, including up to one extra row. The helper trims the extra row and reverses `Before` results into display order. Choose a key that is stable, unique and not null. `hasMore` tells you whether more rows exist in the requested direction.

A cursor page looks like this as JSON:

```json
{ "data": [ { "id": 21 } ], "perPage": 20, "nextCursor": "eyJ...", "previousCursor": null, "hasMore": true, "cursorName": "cursor" }
```

A cursor is the URL-safe base64 form of a compact JSON array: the direction (`"a"` or `"b"`), followed by the key values. The supported key types are `int`, `long`, `Guid`, `string`, `DateTime`, `DateTimeOffset`, `decimal`, `double`, and `bool`. Cursors are **not tamper-proof**. If your app needs tamper protection, wrap the cursors with `IDataProtector`.

A malformed or foreign nonempty cursor falls back to the first page. When `CursorRequest.Logger` is set, the paginator logs the fallback at Debug level, without cursor values or exception details. Minimal API binding uses the configured pagination options. It also supplies a logger in the `Inertia.Net.Pagination` category when a logger factory is available. In MVC or FastEndpoints, `CursorRequest.From(HttpContext)` does the same. Its optional arguments override the configured defaults and parameter names. For a query-only request, or a request created outside HTTP, supply an app logger yourself:

```csharp
var request = CursorRequest.From(query) with { Logger = logger };
```

`Logger` is ignored by JSON serialization and does not affect request equality.

### With EF Core

Install `Inertia.Net.EntityFrameworkCore` to page EF Core queries:

```csharp
using Inertia.Net;
using Microsoft.EntityFrameworkCore;

// Numbered pages: order first, including a unique tie-breaker if needed.
var page = await db.Posts.OrderByDescending(p => p.Id)
    .ToPageAsync(request, ct: ct);

// Cursor pages: pass an unordered query. The helper applies the ordering.
var cursorPage = await db.Posts
    .ToCursorPageAsync(p => p.CreatedAt, p => p.Id, cursorRequest,
        descending: true, ct: ct);

return Render("Feed", new InertiaProps { ["posts"] = Scroll(cursorPage) });
```

`ToPageAsync` requires an ordered query and counts rows by default. As with `PaginateAsync`, the count is skipped when the page already gives the total. Pass `countTotal: false` to skip the count. `total` and `lastPage` are then null. `ToCursorPageAsync` rejects a query that is already ordered. It orders by the supplied key and the optional tie-breaker itself. Both guards throw `InvalidOperationException`. The cursor helper fetches in both directions without counting.

Choose a stable, fully unique ordering: use a unique key, or add a unique tie-breaker when the first key can repeat. Nullable keys are not supported. The cursor codec supports the key types listed in the Cursors section. However, EF translation and ordering for each type depend on the database provider. For example, SQLite needs a suitable value conversion to order `DateTimeOffset`. The page parameter uses the name from a bound request or from `From(...)`. An explicit `pageName` or `cursorName` argument takes precedence.

The EF Core package is not marked Native AOT compatible. EF Core has its own compiled-model requirements. For Native AOT applications, use the core delegate paginators with an AOT-compatible data source instead.

### With MongoDB

For a numbered page of DTOs, call `find.Project(...).ToProjectedPageAsync(request, ct: ct)` on the sorted query. Only the projected fields are fetched.

The `Inertia.Net.MongoDB` package pages a `collection.Find(filter)` in the same way that the EF Core package pages a query:

```csharp
var page = await posts.Find(p => p.Published).SortByDescending(p => p.Id).ToPageAsync(request, ct: ct);
var feed = await posts.Find(p => p.Published).ToCursorPageAsync(p => p.CreatedAt, p => p.Id, cursorRequest, descending: true, ct: ct);
```

The rules are the same as for the EF Core helpers. `ToPageAsync` needs a sorted find. `ToCursorPageAsync` needs an unsorted find,
because it adds the keyset condition to your filter and applies its own sort. Keys may also be `ObjectId`. MongoDB orders strings
by their UTF-8 bytes. The driver does not support trimming, so this package does not support trimming either. See the
[package README](src/Inertia.Net.MongoDB/README.md).

### Pagination under Native AOT

Register each closed page type, such as `[JsonSerializable(typeof(Page<PostDto>))]` and `[JsonSerializable(typeof(CursorPage<PostDto>))]`, in your app's `JsonSerializerContext` along with the item types. See [Native AOT](#native-aot).

---

## Live props

A live prop refreshes itself when the server broadcasts an event. Use a live prop for a value that must stay current while the
page is open. The page response lists which props listen to which events, under `liveProps`. The client subscribes to the
channels. When an event arrives, the client reloads those props with a partial reload, or applies the values that the broadcast
carries without a request. You do not write code to subscribe or to clean up in your components.

Live props need `@inertiajs/react`, `@inertiajs/vue3` or `@inertiajs/svelte` **3.11.0 or later**. Older clients ignore `liveProps`.

**1. Define each event once.** A `LiveEvent` is an event name on a `LiveChannel`. You use the same `LiveEvent` value to mark a
prop live and to publish:

```csharp
public static class OrderEvents
{
    public static LiveEvent Updated(Guid id) => new("OrderUpdated", LiveChannel.Private($"orders.{id}"));
    public static LiveEvent StatsChanged { get; } = new("StatsChanged", LiveChannel.Public("stats"));
}
```

The channel types are `Public`, `Private`, `Presence` and `EncryptedPrivate`. Channel names have no prefix. A transport, which
delivers the messages, adds `private-` or `presence-` when its protocol needs the prefix.

**2. Mark props live.**

```csharp
app.MapGet("/orders/{id:guid}", (Guid id, AppDb db) => Render("Orders/Show", new InertiaProps
{
    ["order"]   = Prop(ct => db.Orders.SingleAsync(o => o.Id == id, ct))
                      .Live(OrderEvents.Updated(id))
                      .Throttle(TimeSpan.FromMilliseconds(500)),
    ["metrics"] = Defer(ct => metrics.ForOrderAsync(id, ct)).Live(OrderEvents.Updated(id)),
    ["stats"]   = Optional(() => stats.Today()).Live(OrderEvents.StatsChanged),
}));
```

- You can call `Live(...)` more than once: each call adds a listener. `.Live(channel, "A", "B")` listens to several events on one
  channel. `Live(() => ..., event)` is the factory form.
- `Live(...)` combines with `Defer`, `Optional`, `Once`, `Always`, `Rescue`, `Parallel`, `Cache` and shared props. A prop that is left
  out of a response still reports its listeners. This covers a deferred prop, an optional prop, a once prop that the client holds,
  and a rescued loader.
- **Merge, deep-merge and scroll props cannot be live.** Marking one live throws `InvalidOperationException`, because a live prop is
  replaced whole.
- `Throttle(window)` sets the shortest time between two reloads of the prop. The client's default is one second.
- A live prop inside an array is reloaded with its whole array ([deviation 11](docs/protocol.md#deliberate-deviations-from-laravel)).
  Use keyed objects for lists that change often.

Shared props can be live, which is the usual way to keep a notification bell current on every page:

```csharp
builder.Services.AddInertia(o => o.Share("notifications", ctx =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    return userId is null ? null : Prop(ct => notifications.SummaryAsync(userId, ct)).Live(new LiveEvent("NotificationCreated", LiveChannel.Private($"users.{userId}")));
}));
```

**3. Publish.** `AddInertiaLive()` registers `IInertiaLive`, which you publish with. A transport registers the broadcaster that
delivers the messages.

```csharp
builder.Services.AddInertiaLive();   // then a transport, e.g. .AddSignalR() or your own IInertiaLiveBroadcaster

// Everyone else on the channel reloads `order`; the caller already has the response.
app.MapPost("/orders/{id:guid}/ship", async (Guid id, HttpContext context, IInertiaLive live) =>
{
    await orders.ShipAsync(id);
    await live.PublishAsync(OrderEvents.Updated(id), exceptSocketId: context.InertiaRequest().SocketId);
    return Back();
});

// From a background job, with values: clients apply them without a request.
await live.PublishAsync(OrderEvents.Updated(order.Id), new { order = OrderDto.From(order) });
```

- When you publish without values, every client reloads the live props that listen to the event. When you publish with values, the
  client updates those props in place and reloads the live props that the values leave out. The values are an object or dictionary
  keyed by prop path, `"auth.user"` included.
- Inertia.Net serializes the values with the page serializer options, so the values match what a reload returns. Under Native AOT,
  this works for types registered in the context that you chain into the HTTP JSON options, as for any prop type. One overload
  takes a `JsonTypeInfo<T>`: `live.PublishAsync(evt, dto, AppJsonContext.Default.OrderProps)`. That overload uses the options of
  that metadata instead, as `JsonSerializer` does.
- **Every subscriber of the channel receives the same values.** Only publish values that everyone allowed on the channel may see.
  Otherwise, publish without values, so that each client reloads its own data.
- `exceptSocketId` leaves one connection out. The client sends its socket id in the `X-Socket-Id` header. Read the id with
  `HttpContext.InertiaRequest().SocketId`. The client supplies this id, so Inertia.Net only ever uses it to exclude a connection.
- A [cached](#cached-props) live prop reloads the cached value. Remove the cached value with `IInertiaCache` before you publish.

**Broadcast prop definitions:** to publish values from prop definitions, first resolve the definitions with
`await BroadcastPropsAsync(http, props)`. Then pass the returned JSON object to `IInertiaLive.PublishAsync`. In this step:

- optional, deferred and once values resolve regardless of visit filters
- rescued values are omitted
- dot-path keys stay flat
- no page or DevTools entry is created

On the client, read a live prop like any other prop. `useProp` exposes the loading state of the prop. A `router.on('live', ...)`
listener can veto an update:

```tsx
import { router, useProp } from '@inertiajs/react'

const order = useProp<Order>('order')   // { value, loading, loaded }
router.on('live', (event) => (event.detail.props.includes('posts') ? false : undefined))
```

### SignalR

The `Inertia.Net.Live.SignalR` package runs the transport inside your app. The transport is a SignalR hub that the browser
subscribes to, with channel authorization.

```csharp
builder.Services.AddInertiaLive().AddSignalR(o =>
    o.Authorize((context, channel, ct) => ValueTask.FromResult(channel.Name == $"users.{context.UserIdentifier}")));
app.MapInertiaLiveHub();   // /inertia-live
```

```ts
import { signalr } from './lib/inertia-signalr'   // dotnet new inertia apps already include it (uncomment `live: signalr()` in app.tsx); elsewhere copy the file and add @microsoft/signalr
createInertiaApp({ live: signalr() })
```

Public channels are open. Private, presence and encrypted-private channels need the authorizer to allow them. The hub:

- refuses other origins with a 403
- closes connections when their authentication expires
- works with a Redis backplane or Azure SignalR Service

See the [package README](src/Inertia.Net.Live.SignalR/README.md) for the options, scale-out and security notes.

### Reloading scroll and merge props

A `Scroll(...)` or `Merge(...)` prop cannot be live, because a live reload would merge the reloaded page into the list. Reload
such a prop from the page instead, with the helper in `ClientApp/lib/inertia-live-reload.ts`. Apps created with
`dotnet new inertia` include this file. Export the event to TypeScript, then call the helper in the page:

```csharp
o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts";
o.TypeScript.LiveEvents(Events.PostCreated);   // exported from routes.ts as liveEvents.PostCreated
```

```tsx
import { liveReload } from '../lib/inertia-live-reload'
import { live } from '../lib/live'                 // the value given to createInertiaApp({ live })
import { liveEvents } from '../types/routes'

useEffect(() => liveReload(live, liveEvents.PostCreated, { only: ['posts'], reset: ['posts'] }), [])
```

The helper works as follows:

- Events within `debounce` milliseconds become one reload. The default is 250.
- A reload waits for a visit that is in flight.
- The scroll position is kept.
- The client that caused the event (`exceptSocketId`) is not reloaded.

`reset` replaces the prop instead of merging it, so the reload brings no duplicates.

### Other transports

The page metadata and the broadcast payload do not depend on the transport, so you can use a transport other than SignalR:

- **Pusher-protocol servers** (Reverb, Soketi, Ably, Pusher) work with the client's built-in `pusher()` or `echo()` transport.
  Implement `IInertiaLiveBroadcaster` to send `LiveMessage.Payload` with your server's publish API.
- **Action Cable** or anything else: implement `IInertiaLiveBroadcaster` (one method) and a client `LiveTransport`.

The broadcast payload is `{"__inertia":{"event":"OrderUpdated","props":{...}}}`. See [docs/protocol.md](docs/protocol.md#broadcast-payload).

---

## Validation and error bags

An Inertia form posts its data, expects a redirect, and reads validation errors from `props.errors` on the next page. When validation fails, Inertia.Net does the redirect and carries the errors across it. See [Flash data](#flash-data) for how this state is stored.

- A field maps to its **first** message. With `o.WithAllErrors = true`, a field maps to an array of all its messages. The client's `form.withAllErrors()` expects that array.
- Error keys are the JSON names of the model members. The naming policy converts each segment once: `Address.Street` becomes `address.street`. (`[JsonPropertyName]` overrides are not applied to these keys.)
- **Error bags.** An error bag is a named group of errors. The client sends `X-Inertia-Error-Bag: createUser` when the form has `errorBag="createUser"`. Errors then arrive as `errors: { createUser: { email: "..." } }`. Without the header, `errors` is the flat object `{ email: "..." }`. A bag stored under a name other than `default` is always sent nested by bag name.

```ts
router.post('/users', data, { errorBag: 'createUser' })
```

Validation keys use client JSON names and dotted collection indices. Minimal API and MVC use the page serializer’s HTTP JSON options. FastEndpoints uses its binding serializer policy. Explicit JSON aliases apply where metadata is available; source-generated metadata is needed for reliable aliases under Native AOT. See the [validation guide](docs/Inertia.Net.Docs/content/docs/validation-and-errors.mdx) for details.

| Validation source | Integration |
|---|---|
| .NET 10 validation | `AddValidation()` and `WithInertiaValidation()` or `WithInertiaPrecognition()` |
| FastEndpoints / FluentValidation | `ConfigureInertia()` uses FastEndpoints validation |
| Other validators | Invoke them explicitly and return `ValidationProblem` with client-facing JSON keys |

For example, call a registered FluentValidation `IValidator<T>` yourself in a Minimal API handler and return client-facing keys:

```csharp
app.MapPost("/users", async (CreateUserInput input, FluentValidation.IValidator<CreateUserInput> validator) =>
{
    var result = await validator.ValidateAsync(input);
    if (!result.IsValid)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email."] });
    return Results.Redirect("/users");
}).WithInertiaValidation();
```

This example maps the validation failure to the JSON field name `email`. In your app, map all relevant failures for your form. `WithInertiaValidation()` still requires `AddValidation()`.

### Minimal API

`WithInertiaValidation()` applies to a route or route group and requires `services.AddValidation()`. Without validation resolvers, it throws `InvalidOperationException` when the endpoint is constructed. Routing may construct endpoints lazily, so the exception does not always happen during application startup.

```csharp
builder.Services.AddValidation();

app.MapPost("/users", (CreateUserInput input, AppDb db) =>
{
    db.Add(input);
    return Results.Redirect("/users");
}).WithInertiaValidation();

app.MapGroup("/admin").WithInertiaValidation().MapPost("/roles", (RoleInput input) => Results.Redirect("/admin/roles"));
```

For an Inertia request that is not a GET, a validation failure becomes a redirect back with the errors. The redirect status is 303 for PUT, PATCH, DELETE and QUERY. Every other client still gets the normal `400` validation problem. When a handler returns `TypedResults.ValidationProblem(...)`, `Results.ValidationProblem(...)` or an `HttpValidationProblemDetails`, Inertia.Net converts the result the same way.

**Why not rely on `AddValidation()` alone?** The built-in .NET 10 validation filter is added automatically and always runs outermost. It writes the 400 itself before any filter you add can see the failure, so you cannot wrap it. `WithInertiaValidation()` disables the built-in filter on the endpoint and runs the same source-generated validation itself. Do not combine `WithInertiaValidation()` with your own `.DisableValidation()` call.

To report errors yourself, with no attribute validation:

```csharp
app.MapPost("/login", (LoginInput input, SignInService auth) =>
    auth.TryLogin(input)
        ? Results.Redirect("/")
        : Back().WithErrors(new Dictionary<string, string> { ["email"] = "These credentials do not match." }));
```

`Back().WithErrors(IDictionary<string, string[]> errors, string bag = "default")` also takes arrays of messages.

### MVC

`AddInertiaMvc()` registers a global filter. When an Inertia request that is not a GET has an invalid `ModelState`, the filter redirects back with the errors before the action runs. The errors go in the bag named by `X-Inertia-Error-Bag`. The default bag is `default`. `[ApiController]` controllers work too: for Inertia requests, the same redirect replaces their automatic 400. Requests that are not Inertia requests keep the standard behavior.

```csharp
[HttpPost("/users")]
public IActionResult Store([FromBody] CreateUserInput input)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);   // reached by non-Inertia clients only
    db.Add(input);
    return Redirect("/users");
}
```

MVC uses the page serializer’s HTTP JSON naming policy for keys. Binding failures get the generic message `The value is invalid.`, because their exception messages can expose internals.

### FastEndpoints

`c.ConfigureInertia()` adds a global pre-processor. When an Inertia request that is not a GET fails validation, the pre-processor redirects back with the errors. Other requests keep FastEndpoints' 400 JSON. FluentValidation validators work as usual.

```csharp
public sealed class CreateUserValidator : Validator<CreateUserRequest>
{
    public CreateUserValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

app.UseFastEndpoints(c => c.ConfigureInertia(ep => ep.AllowAnonymous()));   // the argument is your own per-endpoint configuration
```

An existing `c.Endpoints.Configurator` runs first, followed by the Inertia setup and its optional callback. The Inertia pre-processor runs after an endpoint's own pre-processors. If one of those has already answered, for example with a 403, that answer wins.

### Precognition (live validation)

Precognition validates a form as the user types, without running the handler. Use it for live validation. The server validates a request that has the `Precognition: true` header and answers with:

- `204` and `Precognition-Success: true` when the request is valid
- `422` and `{ "message": "...", "errors": { "field": ["..."] } }` when the request is not valid, limited to the fields named in `Precognition-Validate-Only`
- `Precognition: true` and `Vary: Precognition` on every response

```csharp
// Minimal API: use it instead of WithInertiaValidation(), not in addition.
app.MapPost("/users", (CreateUserInput input) => Results.Redirect("/users")).WithInertiaPrecognition();

// FastEndpoints: nothing to add, c.ConfigureInertia() already answers Precognition requests.
```

`WithInertiaPrecognition()` does everything that `WithInertiaValidation()` does, so normal Inertia validation redirects keep working. It also requires `services.AddValidation()`. An endpoint filter that you add after `WithInertiaPrecognition()` does not run for a precognitive request. Precognition is not available in MVC.

Rate-limit both validation and submissions. Only grant a separate bounded validation bucket when `InertiaPrecognition.IsPrecognitive(request)` is true **and** the endpoint has `InertiaPrecognitionMetadata`. The header alone does not prove that the handler will be skipped. On a 429, add `Precognition: true` for precognitive requests, so that the client recognizes the response. The request is still rejected, so show a retry or error message. See the [Precognition guide](docs/Inertia.Net.Docs/content/docs/precognition.mdx) for a complete policy.

The [sample login policy](samples/MinimalApi.React/LoginDemo.cs) uses a bounded sliding window for validation and a stricter fixed window for submissions. This excerpt omits the test-reset bookkeeping of the sample. To use the excerpt, add `using System.Threading.RateLimiting;` and `using Microsoft.AspNetCore.RateLimiting;`:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", http =>
    {
        var validation = http.GetEndpoint()?.Metadata.GetMetadata<InertiaPrecognitionMetadata>() is not null
            && InertiaPrecognition.IsPrecognitive(http.Request);
        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (validation)
            return RateLimitPartition.GetSlidingWindowLimiter($"validation:{client}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0,
            });
        return RateLimitPartition.GetFixedWindowLimiter($"submit:{client}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        });
    });
    // Precognition: true on validation; a rejected submission goes back with flash.error (or the 429 error page).
    options.OnRejected = InertiaRateLimiting.OnRejected();
});
// After app.UseInertia(), so the flash message is saved: app.UseRateLimiter();
// MapPost(...).WithInertiaPrecognition().RequireRateLimiting("login");
```

Behind proxies, partition by a trusted client identity or by correctly configured forwarded headers. Tune the limits for your application.

On the client, Inertia v3 has Precognition built in:

```tsx
const form = useForm({ name: '', email: '' }).withPrecognition('post', '/users')
// <input onBlur={() => form.validate('email')} />
```

---

## Flash data

Flash data is data that you set during one request for the next page to show once, such as a message after a form post. Inertia.Net carries flash data across the redirect, and across any further redirects. The next response that is not a redirect consumes it. Flash data appears as `page.flash`, and only when there is some.

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

On the client, read flash data with `usePage().flash` or with the `onFlash` visit callback.

To remove flash messages before you render or redirect, call `http.Inertia().ForgetFlashed("toast", "user.name")` in the handler. Exact dotted keys take precedence over nested paths. This call works for restored flash too, and does not extend the lifetime of the remaining messages.

Inertia forms keep their input on the client after `Back().WithErrors(...)`, so there is no `WithInput()` helper. For a full-page POST that needs old input, explicitly flash only sanitized fields: `Back().WithFlash("old", new InertiaProps { ["email"] = email })`. Then read `usePage().flash.old`. Never flash passwords, tokens or other secrets.

**Where state is kept.** The state is the flash data, the errors, `clearHistory` and `preserveFragment` that are set during a request. When that request ends in a redirect, or in a `Location()` or fragment `409`, Inertia.Net saves the state in an `IInertiaStateStore` and loads it at the start of the next request. If the next request redirects again, the state is carried on. This means that `Back()` to a URL that itself redirects still shows the flash. Otherwise, the response of the next request consumes the state. The state is also kept when the response is the version-mismatch `409`.

- **Default: an encrypted cookie** (`.Inertia.State`). The cookie is protected with ASP.NET Core Data Protection, and is HttpOnly and SameSite=Lax. It works without sessions and under Native AOT. Cookies that were tampered with or cannot be decrypted are ignored and deleted. Inertia.Net logs a warning when the cookie is larger than about 4 KB, because browsers drop larger cookies.
- **Session store (opt in):** call `o.State.UseSession()`, with `services.AddSession()` and `app.UseSession()` before `app.UseInertia()`.
- **TempData store (MVC package):** `AddInertiaMvc(o => o.UseTempDataStateStore())` keeps the state in MVC TempData, so the state follows your TempData provider. That provider is the default cookie provider, or `AddSessionStateTempDataProvider()` with `AddSession()` and `app.UseSession()` before `app.UseInertia()`. This store works for Minimal API endpoints too, because the store saves TempData itself. This store is not Native AOT compatible.
- **Your own store:** implement `IInertiaStateStore` and register it before `AddInertia`. Its methods are `LoadAsync`, `SaveAsync` and `ClearAsync`, and all of them move opaque bytes.

**Which one?** Use the default cookie unless you have a reason not to. The cookie is stateless and AOT safe, and has a limit of ~4 KB. Choose the session store when flash data can be large or must not reach the browser, and you already run sessions. Choose the TempData store when the app already standardizes on MVC TempData (for example one provider shared with `TempData["..."]` in views), or you want to switch between cookie and session TempData by changing only the provider.

In a multi-instance deployment, configure Data Protection with a shared key ring. Without a shared key ring, one instance cannot read the cookie that another instance wrote.

---

## Redirects, Location and Back

Use a standard redirect to send the user to another page of your app. Use `Back()` to send the user back to the page they came from. Use `Location(url)` to leave the single-page app (SPA) for another URL.

```csharp
return Results.Redirect("/users");                 // standard redirect: works with Inertia
return Back();                                     // to the Referer, else "~/" (the app root, PathBase included)
return Back("/users");                             // to the Referer, else "/users"
return Location("https://billing.example.com");    // leave the SPA
```

The `UseInertia()` middleware does the following for Inertia requests:

| Situation | Result |
|---|---|
| 302 for PUT, PATCH, DELETE or QUERY | changed to **303 See Other**, so the browser follows with a GET |
| Redirect to another origin (scheme, host or port), and the request is not a prefetch | `409` with `X-Inertia-Location: <url>`: the client makes a full visit instead of following it with XHR, which the browser would block. Turn it off with `o.ConvertExternalRedirects = false`. |
| Redirect whose `Location` contains `#`, and the request is not a prefetch | `409` with `X-Inertia-Redirect: <url>` (the client performs a full visit that keeps the fragment) |
| `Location(url)` | `409` with `X-Inertia-Location: <url>` for Inertia requests, a plain `302` otherwise |
| An empty `200` response | redirect back (302, or 303 for PUT/PATCH/DELETE/QUERY), using the same previous-URL and app-root fallback as `Back()` |
| `Back()` | `302` to the `Referer` when it is an http(s) URL with the request's own host and port, otherwise the opt-in previous URL, then the fallback (default `~/`, the app root including `PathBase`) |

The same-origin rule on `Referer` exists so that `Back()` cannot become an open redirect. `Back()` does not compare the scheme, so it keeps working behind a TLS-terminating proxy. On an HTTPS request, an `http` Referer is upgraded to `https`, so going back never downgrades the connection.

### Previous URL

Inertia.Net can remember the URL of the last rendered page in a cookie, so that `Back()` works without a `Referer` header. Set `o.State.RememberPreviousUrl = true` when a `Referrer-Policy: no-referrer` policy or the browser strips `Referer`:

```csharp
builder.Services.AddInertia(o => o.State.RememberPreviousUrl = true);
```

`Back()` and the middleware's empty-response redirect check the same-host `Referer` first, then the remembered URL, then the fallback. `context.Inertia().PreviousUrl` reads the remembered URL. It returns `null` when the option is disabled, or when the URL is absent or invalid. The remembered URL is user-controlled input, and it is validated as a relative path.

Only successful page GET renders with status 200 are remembered, including initial HTML visits. Partial reloads, prefetches, Precognition, non-GET requests and error pages are excluded. The value is the relative `PathBase + Path + QueryString`, without scheme or host. It is stored in the plain `.Inertia.Previous` session cookie. The cookie is `HttpOnly`, `SameSite=Lax`, `Path=/` and essential, and `Secure` follows `o.State.CookieSecurePolicy`. The cookie is written only when the URL changes. Set `o.State.PreviousUrlCookieName` to change the cookie name. The name must differ from `o.State.CookieName`.

This cookie is used with both cookie and session state stores. The remembered URL is **per browser, not per tab**: the most recent page render wins across tabs. The `Referer` still takes precedence when available.

The middleware adds `Vary: X-Inertia` to every response that passes through it. A rendered page, and the version-mismatch `409`,
also vary on every request header that selects their content: `X-Inertia-Version`, `X-Inertia-Partial-Component`,
`X-Inertia-Partial-Data`, `X-Inertia-Partial-Except`, `X-Inertia-Reset`, `X-Inertia-Except-Once-Props`, `X-Inertia-Error-Bag` and
`X-Inertia-Infinite-Scroll-Merge-Intent`. A shared cache (a CDN or proxy) that is allowed to store a page then keeps a full visit
and a partial visit to the same URL apart. The middleware keeps values that are already in `Vary`, and leaves `Vary: *` alone. This
covers protocol variation only. A page that depends on the user (cookies, authorization) must still be sent with
`Cache-Control: private` or `no-store`. If a cache would store too many selector combinations, it is better to configure that
cache to bypass Inertia pages.

**Pages without a handler.** `app.MapInertia("/about", "About", props)` maps GET and HEAD to `Render("About", props)`, like Laravel's
`Route::inertia`. In MVC, `return this.InertiaView(props);` renders the action's own component. The default component is
`{Controller}/{Action}`, for example `Admin/Users/Index` in an `Admin` area. To change this rule, use
`AddInertiaMvc(o => o.ComponentResolver = ctx => ...)`.

**The page URL.** The page object's `url` is PathBase, Path and QueryString. Behind a proxy that rewrites paths, set
`o.UrlResolver = ctx => ...` to return the URL the browser sees.

---

## Error pages

Error pages render routing failures, authorization failures and unhandled exceptions as Inertia pages in production. Use them to show
your own error components for these failures. Register them in Program.cs:

```csharp
builder.Services.AddInertia();
builder.Services.AddInertiaErrorPages(o =>
{
    o.Component = "Error";                         // default
    o.Components[404] = "Errors/NotFound";          // optional per-status component
    o.StatusCodes = [403, 404, 500, 503];           // default
    o.Props = (context, status, exception) => new InertiaProps
    {
        ["requestId"] = context.TraceIdentifier,
    };
});

var app = builder.Build();
app.UseInertiaErrorPages();                        // before UseInertia and the endpoints
app.UseInertia();
```

Create the named components in your client pages directory. Each error component receives:

- `status`;
- the values returned by the optional `Props` callback;
- shared props registered globally or through `context.Inertia().Share(...)`.

The `Props` callback receives the exception for exception responses and `null` for status-code pages. Avoid returning exception
messages or stack traces to the client.

For example, `ClientApp/pages/Error.tsx`:

```tsx
export default function Error({ status }: { status: number }) {
  const title = status === 404 ? 'Page not found'
    : status === 403 ? 'Access denied'
    : status === 503 ? 'Service unavailable'
    : 'Something went wrong'

  return <main><h1>{status}: {title}</h1><a href="/">Return home</a></main>
}
```

The error page handler answers requests that carry `X-Inertia` and navigations that send `Accept: text/html`. The response keeps the
error status. An XHR request receives a JSON page with `X-Inertia: true`. A first visit receives the normal root template. Root views
and server-side rendering (SSR) work as for any other render. When SSR is enabled, an error component must also resolve in your SSR
bundle.

The handler replaces a response only when its status is one of the configured error statuses and the response is empty. The handler
leaves these to their existing handlers:

- API clients;
- existing response bodies;
- redirects;
- Inertia's `409` responses;
- antiforgery's `419` responses.

Exceptions use the status selected by ASP.NET Core's exception middleware. That status is 500 by default. The default statuses are
403, 404, 500 and 503. Extend `StatusCodes` to handle other error statuses. A page that you return explicitly with
`Render(...).WithStatusCode(404)` keeps its component and props.

Error pages are disabled in Development, so the developer exception page and the Inertia debug modal stay available. Set
`o.EnabledInDevelopment = true` to preview your error components. By default, the Inertia client renders Inertia responses whose
status is not 2xx. Your application's `httpException` event handlers or `onHttpException` callbacks can cancel that rendering.

**Already using `UseExceptionHandler()`?** `AddInertiaErrorPages()` registers an `IExceptionHandler` in the existing chain. Register
API-specific handlers first if they should take priority. Use one exception middleware, then install only the status-code part of the
error pages:

```csharp
app.UseExceptionHandler();
app.UseInertiaErrorPages(useExceptionHandler: false);
app.UseInertia();
```

ASP.NET Core 10 suppresses exception diagnostics by default when an `IExceptionHandler` handles the exception. The Inertia
handler follows that policy and emits only a Debug message naming the rendered component. To keep the built-in exception logs
and diagnostics, configure the exception middleware explicitly:

```csharp
builder.Services.Configure<Microsoft.AspNetCore.Builder.ExceptionHandlerOptions>(o =>
    o.SuppressDiagnosticsCallback = _ => false);
```

`AddInertiaErrorPages()` also registers the built-in ProblemDetails service as the exception middleware’s fallback. The Inertia
exception handler declines requests that do not ask for an Inertia page or HTML. API exceptions therefore use the host’s
ProblemDetails fallback and content negotiation. Existing ProblemDetails configuration and earlier application handlers still apply.
Empty status-code responses remain unchanged. When an error page renders, it consumes the loaded flash and validation state, as any
other page response does.

---

## Authentication and rate limiting

This section shows how to make cookie sign-in and rate limiting answer Inertia requests the way the Inertia client expects.

**Cookie sign-in.** Without `UseInertiaRedirects()`, the cookie authentication handler answers an Inertia visit with 401, because the
Inertia client sends `X-Requested-With`. Call `UseInertiaRedirects()` on the cookie options to change this:

- Inertia visits are redirected to the login page, like browser requests. The redirect uses 303 after a `POST`, `PUT`, `PATCH` or
  `DELETE`.
- `fetch` calls and Precognition validation keep their 401.
- When access is denied, the 403 error page renders if `AddInertiaErrorPages()` handles 403. Otherwise the request is redirected to
  `AccessDeniedPath`.

In Program.cs:

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.UseInertiaRedirects();   // after assigning o.Events, if you do: it wraps the handlers set there
    });

app.UseInertiaErrorPages();   // before UseAuthentication(): a 403 renders the Error page
app.UseInertia();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();         // after UseInertia(): see below
app.UseInertiaAntiforgery();
```

**Rate limiting.** `InertiaRateLimiting.OnRejected()` answers a request that the rate limiter rejects in the way the client expects:

- An Inertia form submission is redirected back with a flash message and `Retry-After`. The message is in `flash.error` by default.
- A Precognition request gets 429 with `Precognition: true`.
- Any other request gets 429.

When `AddInertiaErrorPages()` handles 429, Inertia requests get the error page instead. Call `UseRateLimiter()` after `UseInertia()`,
so that the flash message is saved.

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("login", http =>
    {
        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // The metadata check keeps the Precognition header from skipping the limit on an endpoint whose handler runs for it.
        return http.GetEndpoint()?.Metadata.GetMetadata<InertiaPrecognitionMetadata>() is not null && InertiaPrecognition.IsPrecognitive(http.Request)
            ? RateLimitPartition.GetSlidingWindowLimiter("validation:" + client,
                _ => new SlidingWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 })
            : RateLimitPartition.GetFixedWindowLimiter("login:" + client,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) });
    });
    o.OnRejected = InertiaRateLimiting.OnRejected("error", "Too many attempts. Try again later.");
});
app.MapPost("/login", ...).WithInertiaPrecognition().RequireRateLimiting("login");
```

`dotnet new inertia --auth` creates an app with both cookie sign-in and rate limiting. The app also has a login page, a protected dashboard and the signed-in user shared as `auth.user`.

## Head tags

Head tags let you set the page's `<title>`, meta tags and link tags on the server. The tags are in the first HTML response. They need
no JavaScript, so crawlers and link previews see them. The tags also travel with every visit as the `head` prop:

```csharp
return Render("Posts/Show", post).WithHead(h => h
    .Title(post.Title)
    .Meta("description", post.Summary)
    .Property("og:image", post.ImageUrl)
    .Link("canonical", $"https://example.com/posts/{post.Slug}"));

builder.Services.AddInertia(o => o.Head.TitleTemplate = "{0} - My App");   // optional
app.Use((http, next) => { http.Inertia().Head.Meta("robots", "noindex"); return next(http); });   // per request, e.g. middleware
```

```ts
createInertiaApp({ serverHead: true })   // the client keeps the tags up to date on every visit
```

Put `@inertiaHead` (or `<inertia-head />` in Razor) before the template's own `<title>`. Values are HTML-encoded. Each tag has a key,
written as `data-inertia`. The key is `title`, the meta name or property, or the link rel. A later call with the same key replaces
the tag. `Remove(key)` drops a tag that was set earlier, for example by middleware. With SSR, the server-rendered head is used. The
client builds that head from the same prop. `o.Head.PropName` renames the prop.

## History encryption, clearing and fragments

Inertia.Net can tell the client to encrypt its history state, to clear the history state, and to keep the URL fragment across the next redirect. Turn on history encryption for every page in the options, or call the methods on `context.Inertia()` for one response:

```csharp
builder.Services.AddInertia(o => o.EncryptHistory = true);   // every page

context.Inertia().EncryptHistory();                          // this response
context.Inertia().EncryptHistory(false);                     // opt out of the global setting for this response
context.Inertia().ClearHistory();                            // clear the history state, typically before a redirect (e.g. logout)
context.Inertia().PreserveFragment();                        // keep the URL fragment across the next redirect
```

`encryptHistory` is written as `true` only when it is set. It is not carried across redirects. `clearHistory` and `preserveFragment` go through the state store. They apply to the next page that is rendered after the redirect or redirects, and then they are consumed. For example, a logout endpoint that clears the history before it redirects:

```csharp
app.MapPost("/logout", (HttpContext context) =>
{
    context.Inertia().ClearHistory();
    return Results.Redirect("/login");
});
```

---

## Asset versioning

The asset version tells the client when its assets are stale. The server resolves the version in this order and uses the first one that applies:

1. `InertiaOptions.VersionResolver(HttpContext)`, when it returns a value that is not null.
2. Otherwise, `InertiaOptions.Version`.
3. Otherwise, a hash of the Vite manifest: the first 16 bytes of its SHA-256, in hex. The hash is computed once. In Development, it is computed again when the manifest file changes.
4. Otherwise, `""`.

```csharp
o.Version = typeof(Program).Assembly.GetName().Version!.ToString();   // or a build id
o.VersionResolver = ctx => ctx.Request.Headers["X-Tenant"].ToString();   // per request
```

When an Inertia **GET** request carries an `X-Inertia-Version` that differs from the server's version, the server answers `409`. The response carries `X-Inertia-Location`, which holds the request's path and query, and `X-Inertia-Version`. The client then does a full page reload. The version check runs **before your handler**, so the server does no work to render a page that is thrown away. Requests with other methods never get this 409.

---

## Server-side rendering

Server-side rendering (SSR) renders the first visit on a Node server using your client bundle, so crawlers and slow devices get HTML.

**Failure monitoring:** to be told when a render fails, set `o.Ssr.OnRenderFailed = failure => ...` during registration. The
thread-safe callback receives `SsrRenderFailed`. It holds the error, type, hint, timestamp, component, URL and optional renderer
details. The callback runs once, for the final failed attempt, before the fallback to client rendering or the exception in `Required`
mode. If the callback itself fails, the failure is logged and is not sent to the browser. Policy skips still use logs and metrics.
Treat the diagnostics as private: they may contain renderer paths or secrets.

### 1. Build the SSR bundle

```tsx
// resources/js/ssr.tsx
import { createInertiaApp, type ResolvedComponent } from '@inertiajs/react'
import createServer from '@inertiajs/react/server'
import ReactDOMServer from 'react-dom/server'

createServer((page) =>
  createInertiaApp({
    page,
    render: ReactDOMServer.renderToString,
    resolve: (name) => {
      const pages = import.meta.glob<{ default: ResolvedComponent }>('./Pages/**/*.tsx', { eager: true })
      return pages[`./Pages/${name}.tsx`]
    },
    setup: ({ App, props }) => <App {...props} />,
  }),
  { host: '127.0.0.1', port: Number(process.env.INERTIA_SSR_PORT ?? 13714) },
)
```

The client entry then hydrates the page when the server rendered it:

```tsx
// resources/js/app.tsx: replace the setup function
import { createRoot, hydrateRoot } from 'react-dom/client'

setup({ el, App, props }) {
  if (!el) throw new Error('Inertia root element not found')
  if (el.dataset.serverRendered === 'true') hydrateRoot(el, <App {...props} />)
  else createRoot(el).render(<App {...props} />)
},
```

<details><summary>Vue and Svelte SSR entries</summary>

```ts
// Vue: resources/js/ssr.ts
import { createInertiaApp } from '@inertiajs/vue3'
import createServer from '@inertiajs/vue3/server'
import { createSSRApp, h, type DefineComponent } from 'vue'
import { renderToString } from 'vue/server-renderer'

createServer((page) =>
  createInertiaApp({
    page,
    render: renderToString,
    resolve: (name) => {
      const pages = import.meta.glob<{ default: DefineComponent }>('./Pages/**/*.vue', { eager: true })
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
import { createInertiaApp, type ResolvedComponent } from '@inertiajs/svelte'
import createServer from '@inertiajs/svelte/server'
import { render } from 'svelte/server'

createServer((page) =>
  createInertiaApp({
    page,
    resolve: (name) => {
      const pages = import.meta.glob<ResolvedComponent>('./Pages/**/*.svelte', { eager: true })
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

Build both bundles. The SSR build writes a Node script. Run that script with Node:

```json
{ "scripts": { "build": "vite build && vite build --ssr resources/js/ssr.tsx --outDir ssr" } }
```

```sh
node ssr/ssr.js        # listens on http://127.0.0.1:13714
```

The SSR build leaves `node_modules` imports external, so run the SSR server where `node_modules` is installed. For example, install production dependencies in the Docker image using your chosen package manager and lockfile. Start the SSR server in one of two ways:

- Let the app start it with `o.Ssr.UseNodeProcess()`, described below.
- Run it yourself and add its URL to `o.Ssr.Urls`. Use a process manager (systemd, pm2) or a sidecar container (a `docker compose` service).

The entries above listen on the port in `INERTIA_SSR_PORT` when it is set. The app needs this to run [several instances](#let-the-app-run-the-node-server).

### 2. Enable it

Set the SSR options when you register Inertia:

```csharp
builder.Services.AddInertia(o =>
{
    o.Ssr.Mode = SsrMode.Preferred;                  // Disabled (default), Preferred (fall back to the client) or Required (throw)
    o.Ssr.Urls.Add("http://127.0.0.1:13714");       // the default when empty; add more servers to spread the renders
    o.Ssr.ConnectTimeout = TimeSpan.FromSeconds(5);  // connection establishment
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

The mode for a response is decided in this order:

1. The global mode decides first. `Disabled` turns SSR off for every response, and no response can override it.
2. Otherwise, the response's own mode wins.
3. Then `ExcludePaths` applies.
4. Then the global mode applies.

SSR runs only for first visits, where the response is HTML. It never runs for Inertia XHR requests. Inertia.Net posts the page JSON to `{url}/render`. The SSR server answers `{ "head": [...], "body": "..." }`, and Inertia.Net writes that answer into the root view: `@inertiaHead` outputs the head tags joined with `\n`, and `@inertia` outputs the body verbatim.

#### When the server fails

In `Preferred` mode, every SSR failure logs a warning and the page renders on the client. In `Required` mode, a failure throws `InertiaSsrException`. The exception is thrown before the first byte of the response is written, so the app's exception handler can still answer. `InertiaSsrException.Type` says what happened. The `inertia.ssr.reason` metric tag carries the same value:

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

Failures are logged with the SSR server's `hint` and source location, cut to 2 KB. Skips are logged at most once every 30 seconds per reason, with the number of skips left out. So a missing bundle is not logged on every page.

**Circuit breaker.** The circuit breaker pauses an SSR server that keeps failing. Only the failure types marked "yes" in the table above open the circuit. After `CircuitBreaker.FailureThreshold` such failures in a row (default 5), the server is skipped for `CircuitBreaker.BreakDuration` (default 10 s). After the break, one render probes the server. If the probe succeeds, the circuit closes. If the probe fails, another break starts. A page that fails to render because of its own code never pauses the server. A managed process that restarts and passes its health check closes its circuit at once. `CircuitBreaker.Enabled = false` turns the circuit breaker off.

**Render slots.** Render slots limit how many renders one app instance sends to each SSR server at once:

- `Concurrency.MaxActive` is the most renders that each server takes at once from this app instance. The default is 8. `0` means no limit.
- `Concurrency.MaxQueued` is the most renders that wait for a slot. The default is 32.
- `Concurrency.QueueTimeout` is the longest that a render waits for a slot. The default is 250 ms.

A render that finds no slot falls back at once, so waiting renders do not accumulate. The limits apply per app instance: a server behind N instances can get N × `MaxActive` renders.

#### Several SSR servers

Add more than one URL to `o.Ssr.Urls` to spread the renders over several SSR servers:

```csharp
o.Ssr.Urls.Add("http://ssr-1:13714");
o.Ssr.Urls.Add("http://ssr-2:13714");
```

Each render goes to the server that has the fewest renders in flight, among the servers whose circuit is closed. Ties rotate. Every server has its own slots, queue and circuit. Only a refused connection is retried on another server, and the retry stays within the same deadline. A render is not retried after a timeout or an error, because the render may already have run.

**Remote servers.** The renderer must be trusted: page props travel to it, and its HTML goes into your pages unescaped. A plain `http://` URL that is not loopback logs a warning at startup. Use HTTPS or a private network. The `Inertia.Net.Ssr` HTTP client (`InertiaSsr.HttpClientName`) does not follow redirects and does not use a proxy. To add authentication, add a message handler, for example `services.AddHttpClient(InertiaSsr.HttpClientName).AddHttpMessageHandler<SsrAuthHandler>()`. If you replace the client's primary handler, keep `AllowAutoRedirect = false`. The client logs nothing per request. To restore its logging, call `services.AddHttpClient(InertiaSsr.HttpClientName).AddDefaultLogger()` after `AddInertia`.

#### Let the app run the Node server

Call `o.Ssr.UseNodeProcess()` to let the app start, supervise and stop the Node SSR server:

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

When the app starts, a hosted service runs `node {bundle}` once per instance, without a shell. Occupied ports are refused before anything is spawned. The service waits up to `StartupTimeout` for every instance to answer `GET /health`.

The service logs each instance's stdout at Information level and its stderr at Warning level, under the `Inertia.Net.Ssr` category. Each line is cut to 2 KB.

`Ssr.Urls` is not used. Renders go only to the instances that the app owns, and only once one instance is healthy.

A missing bundle, a missing `node` or a startup timeout is logged, and pages render on the client. In `Required` mode, app startup fails with `InertiaSsrException` (`Type = "startup"`).

An instance that exits is restarted after 1 s. The delay doubles, up to 30 s. An instance that stops answering its liveness check is killed and restarted the same way. This catches a blocked event loop, which keeps the process alive.

Shutdown cancels supervision and stops the owned process trees without requesting `/shutdown`. Nothing is spawned while the Vite dev server runs. For external or remote servers, set `Mode` and `Urls` without `UseNodeProcess()`.

- **Ports.** Each instance gets its port in the `INERTIA_SSR_PORT` environment variable. It also gets `NODE_ENV=production`, unless that variable is already set. The entries in step 1 listen on the port in `INERTIA_SSR_PORT`. The v3 server (`createServer` in `@inertiajs/core`) has no CLI flag for the port. A bundle built by `@inertiajs/vite` alone listens on the port from `inertia({ ssr: { port } })`, which is fixed at build time (default `13714`). Such a bundle only works with one instance on that `BasePort`. For `Instances > 1`, use an entry that reads `INERTIA_SSR_PORT`.
- **Keep it on loopback.** The SSR server has no authentication. Anyone who can reach it can send `POST /render`, which uses CPU, or
  `POST /shutdown`. Its `head` and `body` are written into your pages verbatim, so the SSR server must be trusted. Listen on
  `127.0.0.1`. The entries above do. With `@inertiajs/vite`, build with `inertia({ ssr: { host: '127.0.0.1' } })`, because the default
  host of `@inertiajs/vite` is `0.0.0.0`. Before an instance takes renders, Inertia.Net tries the machine's non-loopback addresses on
  the instance's port. If the instance answers there, Inertia.Net stops it, does not restart it, and logs an error
  (`Stopped the SSR server: it also accepts connections on ...`). With `AllowNetworkExposure = true`, the instance keeps running and a
  warning is logged.
- **Orphans.** A graceful shutdown (Ctrl+C, SIGTERM, `docker stop`, IIS recycle) stops Node. If the .NET process is killed outright or crashes hard, the OS does not stop Node with it. The next start of the app then rejects the occupied port. Use a process manager or a container to clean up after a hard crash.

### 3. Development

In development, SSR goes through the Vite dev server. When the Vite hot file is present (Development only), or when `o.Vite.DevServerUrl` is set, Inertia.Net posts to `{viteDevUrl}/__inertia_ssr` instead of the Node SSR server. The `@inertiajs/vite` plugin serves that endpoint, so you need no built bundle and no Node server:

```ts
// vite.config.ts, add to plugins
import inertia from '@inertiajs/vite'
inertia({ ssr: { entry: 'resources/js/ssr.tsx', host: '127.0.0.1', port: 13714 } })
```

`BundlePath` is ignored while the dev server runs.

### 4. Health check

Add the SSR health check to the app's health checks:

```csharp
builder.Services.AddHealthChecks().AddInertiaSsr();   // name "inertia-ssr"; optional failureStatus, tags, timeout
app.MapHealthChecks("/health");
```

The health check does not call the SSR servers. It reports what the renders have already shown:

- Healthy: SSR is disabled, SSR goes through the Vite dev server, or every server can take renders.
- Degraded: some servers' circuits are open, or some managed instances are not ready.
- The registration's failure status (unhealthy by default): no server can take renders.

The check's data lists each server's state: `closed`, `half-open`, `open` or `not-ready`. A server that went down is reported healthy until renders fail.

Stale SSR refreshes share the bounded background queue of [cached props](#cached-props). The queue is limited to 64 items, 4 workers and 30 s per refresh. Dropped work is logged and counted by `inertia.cache.refresh.dropped`. SSR in `Required` mode exposes a generic failure message. Detailed failures are logged.

### 5. Observability

Inertia.Net publishes metrics on the `Inertia.Net` meter and traces on the `Inertia.Net` activity source. The constants `InertiaDiagnostics.MeterName` and `InertiaDiagnostics.ActivitySourceName` hold these names. For example, with OpenTelemetry:

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
| `inertia.cache.refresh.dropped` | counter | `inertia.cache.kind` (`prop`, `ssr`) |

Tag values are bounded: they contain no URLs, component names or user data. The `inertia.render` activity carries `inertia.component`. The `inertia.ssr` activity carries `inertia.ssr.endpoint`, `inertia.ssr.outcome` and `inertia.ssr.reason`. Nothing is recorded unless a listener is attached.

### 6. Response cache

The response cache keeps server-rendered HTML and reuses it. A render depends only on the page JSON. A page that is exactly the same
for many visitors, such as a landing page, a catalog or an article, can therefore reuse its HTML instead of calling the SSR server
again:

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

The cache key is the SHA-256 of the page JSON: component, props, url, version and the rest. A new deploy (another asset version) or
different props therefore never reuse a stale render. The cache is global across requests. Use it only for anonymous content, or
for content that is otherwise safe to share. Pages with per-user props make one entry per user, so cache the pages that are shared.
A cache hit is counted as `inertia.ssr.outcomes{inertia.ssr.outcome=rendered, inertia.ssr.reason=cache}`. A cache hit bypasses the
render slots and circuit breakers. Nothing is cached while the Vite dev server renders.

Concurrent cache misses for the same page share one fill per app instance. The time a request waits for that fill counts against
`Ssr.Timeout`. Cancelling a waiting request does not cancel the request that fills the entry. If that fill fails or is cancelled, a
waiting request can retry.

With `StaleFor` set, a render that is past its `Duration` is still served, and it is counted with `inertia.ssr.reason=stale`. The
page is rendered again after that response has been sent, as for [stale props](#cached-props). Only one refresh runs per page and
app instance at a time. A failed refresh keeps the stale render. `IInertiaCache.ClearSsrAsync()` removes every render. Use it for
an SSR bundle deployed without a new asset version. Renders are kept in the same [`IInertiaCacheStore`](#cached-props) as props.
`MaxBytes` bounds the default store only. Use the [Redis store](#redis) to share renders across app instances.

---

## Vite integration

`ViteAssets` renders the HTML tags that load your Vite assets, for the `@vite(...)` token and the `<vite />` tag helper. `ViteAssets` is a singleton. You can also call it directly: `vite.RenderTags(httpContext, "resources/js/app.tsx")`.

**Production.** `ViteAssets` reads `wwwroot/build/.vite/manifest.json` once and pre-renders the tags to UTF-8. For Vite 4 and older, it falls back to `wwwroot/build/manifest.json`. For each entry it emits these tags, in order:

1. `<link rel="stylesheet">` for the entry's CSS and the CSS of its static imports (recursively, deduplicated);
2. `<script type="module" src="...">` for the entry file;
3. `<link rel="modulepreload">` for each static import.

Asset URLs are `{PathBase}/{BuildDirectory}/{file}`, so apps under a path base work, or `{AssetBaseUrl}/{file}`. An entry that is missing from the manifest throws an exception with the list of available entries. A missing manifest, when there is also no dev server, throws an exception with the paths it looked in.

**Development.** While the Vite dev server runs, the tags are `@vite/client` and the entry from the dev server origin. `@viteReactRefresh` adds the React Refresh preamble. The dev server counts as running when either condition is true:
- `o.Vite.DevServerUrl` is set (always, in any environment), or
- the environment is Development and the hot file (`wwwroot/hot` by default) exists and contains the origin.

Set the Vite options when you register Inertia:

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

Inertia.Net accepts only plain http or https origins, without quotes or markup, from the hot file or `DevServerUrl`. The reason is that the origin is written into a script.

When `AssetBaseUrl` is set, Vite's own `base` must match it, so that lazily imported chunks also resolve against the CDN. For example: `base: 'https://cdn.example.com/build/'`.

**Prefetching.** Page components resolved with lazy `import.meta.glob` (the `@inertiajs/vite` default) are separate chunks. The
browser downloads a chunk only when a page needs it. `o.Vite.Prefetch` downloads the chunks after the page has loaded, at low priority,
so the next navigation does not wait for them. Laravel's equivalent is `Vite::useWaterfallPrefetching`:

```csharp
o.Vite.Prefetch = VitePrefetch.Waterfall;   // PrefetchConcurrency (default 3) at a time; VitePrefetch.Aggressive starts all at once
```

With prefetching on, the tags get one small inline script. The script lists the chunks that the entries can import later: dynamic
imports, then their static imports and CSS, minus the chunks that are already loaded. The script is rendered once per entry set, like
the other tags.

### Content Security Policy

If your app uses a nonce-based Content Security Policy (CSP), set `o.CspNonce` to the request's nonce. Every script and stylesheet tag
that Inertia.Net writes then carries the nonce:

- the page `<script data-page>`;
- the Vite tags;
- the React refresh preamble;
- the prefetch script, whose links also get the nonce.

The MVC tag helpers follow the same option.

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

The nonce must be base64 or base64url. Any other value throws an exception. The cached tags are split at the places where the nonce
goes. A nonce therefore costs one copy of the tag bytes per request and no re-rendering.

---

## CSRF and antiforgery

Inertia.Net protects forms against cross-site request forgery (CSRF) with one middleware call. The Inertia client reads the
`XSRF-TOKEN` cookie and sends its value as the `X-XSRF-TOKEN` header on every request. `UseInertiaAntiforgery()` issues that cookie
and validates it, so one call protects every form:

```csharp
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);   // "X-XSRF-TOKEN"

app.UseAuthentication();                 // if the app signs users in: the token is bound to the signed-in user
app.UseInertia();
app.UseInertiaAntiforgery();
```

- **Issue.** Page loads get the request token in a cookie. A page load is a GET or HEAD request that is an Inertia request or accepts
  `text/html`. The cookie is script-readable and essential: `HttpOnly` off, `SameSite=Lax`, `Path=/`, and `Secure` as set by
  `SecurePolicy`. Essential means that a cookie-consent policy (`CheckConsentNeeded`) does not withhold the cookie. Withholding it
  would make every form submission fail.
- **Validate.** Every unsafe request is checked with `IAntiforgery.ValidateRequestAsync`. An unsafe request is any request whose
  method is not GET, HEAD, OPTIONS or TRACE. The check applies to requests with a JSON body or a form body, whether they are Inertia
  requests or not. A plain cross-site HTML form post is rejected, and so is a forged XHR. The middleware also sets the antiforgery
  feature, so form-binding endpoints work without `app.UseAntiforgery()`.
- **Reject.** A request with a missing or wrong token gets **`419`** (page expired, as in Laravel). The endpoint handler does not run.
- **Opt out** per endpoint with `.DisableAntiforgery()`, for example for webhooks and test hooks. Opt out for whole areas with
  `ShouldValidate`.

**.NET 11.** Apps built with `WebApplication.CreateBuilder` also get the framework's automatic CSRF middleware. That middleware checks
`Sec-Fetch-Site` and `Origin` on unsafe requests. It issues no tokens and only records a verdict. `UseInertiaAntiforgery()` remains the
token check that Inertia needs. Like `app.UseAntiforgery()`, it replaces the framework's verdict with its own. A request with a valid
token passes, and a request without a valid token still gets `419`. `.DisableAntiforgery()` opts an endpoint out of both checks. The
configuration value `DisableCsrfProtection=true` turns the automatic check off.

`UseInertiaAntiforgery()` takes options:

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

**Lazy refresh.** By default, every page load issues a fresh token, and antiforgery marks that response
`Cache-Control: no-cache, no-store`. That header also keeps the page out of the browser's back/forward cache.
`o.CookieRefresh = XsrfCookieRefresh.Lazy` keeps a token that still validates. Responses that keep the token get no `Set-Cookie` and
stay cacheable. A token issued before a sign-in or sign-out is replaced on the next page load. Validating a token costs about as much
as issuing one, so the gain is in the responses, not in CPU. Set your own `Cache-Control` on pages with private data.

The token in an open tab becomes invalid when the session expires or when a sign-in changes the user. The next page load issues a new
token. If `OnRejected` returns `Back()`, that tab gets a fresh token and a message instead of the client's error dialog.

MVC: `UseInertiaAntiforgery()` covers controllers too. `[AutoValidateAntiforgeryToken]` as a global filter still works, but you do not need it.
FastEndpoints: the middleware covers FastEndpoints. You do not need FastEndpoints' own `EnableAntiforgery()` for Inertia forms.

**Issue only.** `UseInertiaAntiforgeryCookie(cookieName = "XSRF-TOKEN")` is the 1.0 API. It only writes the cookie. You must then
validate yourself: use `app.UseAntiforgery()`, which covers form bodies only, and add your own check for JSON bodies
(`await antiforgery.ValidateRequestAsync(context)`). Prefer `UseInertiaAntiforgery()`.

---

## Deploying behind a proxy

When the app runs behind a load balancer or reverse proxy that terminates TLS, Kestrel sees plain HTTP unless the scheme is forwarded.
If the scheme is not forwarded, the state cookie and the `XSRF-TOKEN` cookie are not marked `Secure`. To forward the scheme, configure
forwarded headers:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // o.KnownProxies / o.KnownNetworks: list your proxy, or headers from anyone else are ignored
});

app.UseForwardedHeaders();               // first
app.UseInertia();
```

When the scheme cannot be forwarded, force both cookies to be `Secure`:

```csharp
builder.Services.AddInertia(o => o.State.CookieSecurePolicy = CookieSecurePolicy.Always);
app.UseInertiaAntiforgery(o => o.SecurePolicy = CookieSecurePolicy.Always);
```

When the app runs on several instances, share the Data Protection key ring between them (`AddDataProtection().PersistKeysTo...`). The
state cookie and the antiforgery tokens are protected with that key ring. Keep the SSR server on loopback on each instance (see
[Server-side rendering](#server-side-rendering)).

### Compression and asset caching

Compression makes page JSON and HTML five to ten times smaller. Compress at the proxy, or in the app. The samples and the template
compress in the app:

```csharp
builder.Services.AddResponseCompression();   // Brotli and gzip; Zstandard too on .NET 11 (which always adds Vary: Accept-Encoding)
app.UseResponseCompression();                // before UseStaticFiles and UseInertia
```

Over HTTPS, the response compression middleware compresses only with `o.EnableForHttps = true`. Review the BREACH attack for your
pages before you set it. Inertia.Net keeps the antiforgery token in a cookie, not in the body. But a page that reflects request input
next to a secret is still exposed.

Vite puts a content hash in every built file name, so the files under `build/assets` can be cached permanently:

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

`app.MapStaticAssets()` does the same and adds build-time Brotli. For this, build the client before `dotnet publish`, so that the
files are in `wwwroot` when the static web assets manifest is generated.

---

## Multiple apps and libraries

Use a **mount** to run a second Inertia UI in the same app, such as a dashboard library mounted under `/dashboard`. A mount is a
named `InertiaOptions` that `app.UseInertia(name)` selects in a `Map` branch. Each mount has its own:

- shared props;
- asset version (the 409 check only compares against the mount's version);
- state and previous-URL cookies (`.Inertia.State.{name}`, `.Inertia.Previous.{name}`);
- root view;
- Vite manifest;
- SSR servers;
- TypeScript output;
- error pages.

The default mount is the one that `AddInertia(...)` registers and `UseInertia()` uses.

```csharp
builder.Services.AddInertia(o => o.Share("user", ctx => ctx.User.Identity?.Name));      // the host app
builder.Services.AddInertia("dashboard", o =>
{
    o.Version = "dashboard-2.1.0";
    o.Share("product", "Dashboard");
});
builder.Services.AddKeyedSingleton<IInertiaRootView, DashboardRootView>("dashboard");   // optional: an embedded template
builder.Services.AddInertiaErrorPages("dashboard", o => o.Component = "Dashboard/Error"); // optional

var app = builder.Build();
app.Map("/dashboard", dashboard =>
{
    dashboard.UseInertia("dashboard");
    dashboard.UseRouting();
    dashboard.UseEndpoints(e => e.MapGet("/", () => Render("Dashboard/Index")));
});
app.UseInertia();
```

`Map` sets the path base, so page URLs, `Back()` fallbacks and asset URLs stay under `/dashboard`. Startup fails when
`UseInertia(name)` names a mount that is not registered, or when two mounts share a cookie name. All mounts share the cache, live
props, metrics, DevTools and antiforgery. Named mounts add an `inertia.mount` tag to the metrics.
`healthChecks.AddInertiaSsrForMount(name)` checks the SSR servers of one mount. The page protocol does not change.

---

## Native AOT

Inertia.Net supports publishing with Native AOT. `Inertia.Net`, `Inertia.Net.FastEndpoints` and `Inertia.Net.Live.SignalR` are annotated `IsAotCompatible`. CI publishes a real app with Native AOT (`tests/Inertia.Net.AotSmoke`), with zero trim and AOT warnings allowed. The library never uses reflection on your props: it writes values with `JsonTypeInfo` from your `JsonSerializerContext`.

**1. Register an app `JsonSerializerContext` for every prop type**, and chain it into the HTTP JSON options:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

[JsonSerializable(typeof(UsersPage))]               // typed page props
[JsonSerializable(typeof(Page<UserDto>))]           // closed pagination types you return
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]           // closed collection types you return
[JsonSerializable(typeof(UserDto[]))]
[JsonSerializable(typeof(Stats))]                   // the T of Defer(() => new Stats(...)) and friends
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

Register these types in the context:

- the type that each loader returns;
- the type of each plain value you place in `InertiaProps`;
- flash values that are not plain strings or numbers, because flash is serialized through the same options.

The library's own context already covers `string`, `bool`, `int`, `long`, `double`, `decimal`, `float`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `JsonElement`, `string[]`, `int[]`, `List<string>`, `Dictionary<string, string>`, `Dictionary<string, string[]>`, `InertiaProps` and `Dictionary<string, object>`, so those types need no registration. The source generator accepts members of type `InertiaProp` without any attribute.

**2. Validation model types must be public.** The .NET 10 `AddValidation()` source generator skips internal types, and validation then silently does nothing. Only one `AddValidation()` call site per project is allowed.

**3. Call `services.AddProblemDetails()`.** The non-Inertia `400` validation problem needs the `ProblemDetails` JSON metadata, or it cannot be serialized without reflection.

**4. Anonymous types do not work as props, flash values or published live values** under AOT. Publish [live prop](#live-props) values as registered records or classes. The `IInertiaLive.PublishAsync` overload that takes a `JsonTypeInfo<T>` also works for types outside the HTTP JSON context. Use `InertiaProps`, records or classes registered in the context. `Inertia.Net.Live.SignalR` is AOT compatible. SignalR itself does not support strongly typed hubs (`Hub<T>`) under AOT, so keep any hubs of your own untyped.

**5. Content files.** `app.html` and the Vite manifest are read from disk at run time. Copy them to the publish directory. Set the content root to the binary's directory when the binary may be started from another directory:

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

**6. MVC is not AOT compatible.** ASP.NET Core MVC itself is not AOT compatible, so `Inertia.Net.Mvc` is not either. Use Minimal APIs or FastEndpoints for AOT.

**7. FastEndpoints** needs its source generator discovery instead of reflection scanning:

```csharp
builder.Services.AddFastEndpoints(new[] { DiscoveredTypes.All });
```

The `FastEndpoints.Generator` package generates `DiscoveredTypes` in the namespace named after the assembly. See [samples/FastEndpoints.Svelte](samples/FastEndpoints.Svelte).

**8. A smaller binary.** The SSR client never uses HTTP/3, so you can trim HTTP/3 support out of the binary:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.Net.SocketsHttpHandler.Http3Support" Value="false" Trim="true" />
</ItemGroup>
```

Cached props, the SSR cache, nonces, prefetching, `MapInertia`, the DevTools recorder and live props over SignalR are all AOT safe and covered by the smoke test.

The reference app is [tests/Inertia.Net.AotSmoke](tests/Inertia.Net.AotSmoke/Program.cs). Run it end to end with `bash eng/aot-smoke.sh`. The script runs on Linux and needs clang and zlib.

---

## TypeScript types

Inertia.Net can write TypeScript declarations for your typed page props, the shared props and the flash data. The declarations follow
the app's JSON contracts, which are the HTTP JSON options and their source-generated contexts. The names, nullability and enum values
are therefore the same as in the page JSON:

```csharp
builder.Services.AddInertia(o =>
{
    o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";   // written when the app starts in Development, only when it changed
    o.TypeScript.Page<UsersPage>("Users/Index").Page<HomePage>("Home");
    o.TypeScript.FlashData<Flash>();                             // optional: the whole page.flash object; SharedProps<T>() too
});

public sealed record UsersPage(List<User> Users, [property: InertiaPropType(typeof(UserStats))] InertiaProp Stats);
```

`FlashData<T>()` types the whole flash object, not one entry: each property of `T` is a flash key. To type
`Flash("toast", new Toast("success", "Saved"))`, wrap the entry:

```csharp
public sealed record Toast(string Type, string Message);
public sealed record Flash(Toast? Toast);   // page.flash.toast is Toast | null
o.TypeScript.FlashData<Flash>();
```

To write the file without running the app normally, for example in CI or outside Development, run the app with `--inertia-types`.
The app then writes the declarations in any environment and stops. It exits with code 1 if the declarations could not be written.
The templates add this command as a script:

```sh
dotnet run -- --inertia-types   # or: npm run types
git diff --exit-code ClientApp/types/inertia.d.ts   # fail CI when the committed file is stale
```

With `--inertia-types`, Program.cs still runs up to `app.Run()`, so startup code such as migrations runs too. The server stops as soon as it has started.

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

- **Shared props** are inferred from the values passed to `o.Share(...)`. Keys with dots nest. A factory is typed by what it returns:
  `o.Share("user", ctx => new UserDto(...))` gives `user: UserDto`. A factory cast to `Func<HttpContext, object?>` is `unknown`.
  Shares added per request (middleware, `HttpContext.Inertia().Share(...)`) are not known at startup. Type them with
  `o.TypeScript.SharedProps<T>()`, which replaces the inference. The `InertiaConfig` augmentation types `usePage().props` and `flash`.
- **Prop members.** An `InertiaProp` member is optional, because it can be deferred, optional or rescued. Its type is `unknown`,
  unless `[InertiaPropType(typeof(T))]` names its value type.
- **Mapping.** C# types map to TypeScript types as follows:
  - Numbers are `number`. With `PreserveBigIntegers`, 64-bit and larger numbers are `number | bigint`.
  - Dates, GUIDs and `byte[]` are `string`.
  - Enums are unions of their JSON values. The values are numbers by default, with the member names in a doc comment. With
    `JsonStringEnumConverter`, the values are string names.
  - Dictionaries are `Record<string, T>`.
  - Polymorphic types are unions tagged with their discriminator.
  - C# 15 `union` types (.NET 11) are unions of their case types (`User | Team`, plus `| null` when a case is nullable).
  - Nullable members get `| null`.
- Types that are missing from the JSON metadata are `unknown`, and a warning is logged. Under Native AOT, register them in your
  context. Commit the generated file, so that `tsc` passes in CI without running the app.
- Number handling follows member settings, then type settings, then the global JSON options. Custom converter output is typed as
  `unknown` with a warning when its shape cannot be inferred.

### Page records

A record that implements `IInertiaPage` names its component once, so neither `Render` nor the TypeScript registration repeats it:

```csharp
public sealed record UsersPage(List<User> Users) : IInertiaPage
{
    public static string Component => "Users/Index";
}

return Render(new UsersPage(users));                 // component "Users/Index"
o.TypeScript.Page<UsersPage>();                      // or AddGeneratedPages(): every IInertiaPage in the project, listed at compile time
```

`AddGeneratedPages()` is written by a source generator, so it needs no reflection and works under Native AOT. `PagesFrom(assembly)`
does the same with reflection, for page records in another assembly.

### Testing live form validation

Use the existing antiforgery setup, then mark the request with `WithPrecognition("items.0.email")`. Omit field names to validate the whole form. Use `response.AssertSuccessfulPrecognition()` for a 204 with the Precognition headers, or `await response.AssertPrecognitionErrorAsync("items.0.email")` for a validation error. Assert separately that the submit handler made no changes.

### First visits and frontend readiness

Set `o.Vite.PageModule = component => $"ClientApp/pages/{component}.tsx"` to preload the current page’s lazy JavaScript on the first HTML visit. Use manifest keys for your project and return `null` for pages to skip. Register `builder.Services.AddHealthChecks().AddInertiaVite(["ClientApp/app.tsx"])` on a production readiness probe to check published frontend files, including dynamic imports. The check runs once per host and supports named mounts. [Vite guide](https://inertia-dotnet.temtem.dev/docs/vite-integration) · [Deployment guide](https://inertia-dotnet.temtem.dev/docs/deploying)

### Typed routes

Use `<Link href={routes.orders.show(42)} instant>` when the named GET route includes its destination component. Named `MapInertia` endpoints supply it automatically when no component transformer is configured. Use `.WithInertiaComponent("Orders/Show")` on a regular endpoint with a fixed destination. The page needs a loading state while the GET is pending.

Set `RoutesOutputPath` to generate a typed function in `routes.ts` for every named endpoint. An endpoint is named with `WithName`, a
controller route name, or a FastEndpoints `Description(x => x.WithName(...))`:

```csharp
o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts";
app.MapGet("/users/{id:int}", ...).WithName("users.show");
app.MapPost("/users", (CreateUser input) => ...).WithName("users.store");
```

```tsx
import { routes, type RouteBodies } from '../types/routes'
<Link href={routes.users.show(user.id)}>...</Link>               // { url: '/users/1', method: 'get' }
const form = useForm<RouteBodies['users.store']>({ name: '' })   // the request body, typed from CreateUser
form.submit(routes.users.store())
routes.users.show.url(1, { query: { tab: 'posts' } })            // '/users/1?tab=posts'
```

The generated file follows these rules:

- Route parameters are typed from their constraints.
- Optional and catch-all route parameters are optional.
- Every HTTP method of an endpoint has a variant, for example `routes.users.update.put(1)`.
- `routeConfig.base` prefixes every URL, for an app under a path base.
- `RouteBodies` holds the `[FromBody]` type (or the FastEndpoints request type) of each route that has one.
- Unnamed endpoints are skipped.

`o.TypeScript.LiveEvents(...)` also exports [live event](#reloading-scroll-and-merge-props) names and channels as `liveEvents`.

### Generating at build time

To write the types on every build, set `<InertiaGenerateTypesOnBuild>true</InertiaGenerateTypesOnBuild>` in the project file. Every
build, including the build inside `dotnet publish`, then runs the app once with `--inertia-types` to write both files. The app starts
without listening on any port, and then exits. If the files cannot be written, the build fails. In CI, compare the result with the
committed files:

```sh
dotnet build -p:InertiaGenerateTypesOnBuild=true
git diff --exit-code ClientApp/types
```

### Compile-time component checks

Inertia.Net ships an analyzer that checks component names when you compile. It has two rules:

- INERTIA001 reports two page records that claim one component.
- INERTIA002 reports a component name that has no page file in the pages directory. It checks the names in `Render("...")`,
  `MapInertia` and page records. This rule needs `<InertiaPagesDirectory>ClientApp/pages</InertiaPagesDirectory>` in the project file.

## DevTools

The [Inertia DevTools](https://inertiajs.com/docs/v3/advanced/devtools) browser extension shows every visit, its props, its headers and
the page state. Inertia.Net implements the server protocol that the extension reads. The code below registers the recorder and maps
its read API:

```csharp
builder.Services.AddInertiaDevTools();   // records in Development only
app.UseInertia();
app.MapInertiaDevTools();                // GET /_inertia/devtools/entries/{id} and /entries
```

The inspector reports absolute request URLs and the application base path. The read API leaves pending flash data and errors untouched.
Entries are kept in memory. Source-call locations (`renderSource`, `shareSource`, `actionSource`) are not collected.

Each request that passes through `UseInertia()` gets an `X-Inertia-Devtools-Id` and an entry in memory. Entries are kept per browser
tab, at most `MaxEntriesPerTab` and `MaxEntries`, for `MaxAge`. An entry holds:

- the request type
- timings
- headers
- bodies
- the page object and its props metadata

The first page carries the discovery tag that the extension reads. Nothing leaves the app. The recorder warns when it is enabled
without a mapped reader endpoint.

The recorder redacts sensitive values and limits what it keeps:

- A value is replaced with `[REDACTED]` in props, bodies and headers when its name contains `password`, `secret`, `token`,
  `authorization`, `cookie` or `api_key` (also `apikey`, `api-key`). Add more names with `RedactedKeys`.
- Sensitive query parameters are also redacted in recorded request and page URLs, redirects and URL headers. The actual application
  URLs are unchanged.
- Malformed JSON bodies are omitted.
- Only eligible Inertia text bodies with a known length up to `MaxBodyBytes` are buffered. Nonseekable bodies of unknown length are
  omitted as streamed, so recording does not spool bulk requests to disk.
- A page larger than `MaxResponseBytes` (default 1 MiB) is recorded without its body and is never copied. The entry is marked
  `too-large` and has the page size in `__meta.responseBytes`. The browser still receives the whole page.
- Kept entries are also limited by `MaxTotalBytes` (default 64 MiB). The oldest entries are dropped first.

Without an `Authorize` callback, the read API answers in Development only, and only requests from the machine that runs the app. A
request counts as coming from that machine when all of these are true:

- The connection is a loopback connection.
- The host name is a loopback host name such as `localhost`.
- The request has no `X-Forwarded-For`, `Forwarded`, `X-Forwarded-Host`, `X-Real-IP` or `Via` header.

Any other request gets a `404`. This rule stops two things from reading other users' entries: an app that runs as Development on a
server by accident, and a DNS-rebinding page in the developer's browser. Set `Authorize` to read entries from a container or from
another machine. A reverse proxy on the same machine that adds none of those headers makes a remote request look local, so set
`Authorize` in that case too.

Outside Development, nothing is recorded unless `Enabled = true`, and the read API answers only requests that `Authorize` accepts. Any
other request gets a `404`. When `Pages.Directory` is set, each entry links the component to its file.

## Development checks

In Development, Inertia.Net can check that every rendered component has a page file. `o.Pages.Directory = "ClientApp/pages"` makes
`Render` throw in Development when the component has no page file. The error names the closest page file
(`'Users/Indx' was not found in 'ClientApp/pages' ... Did you mean 'Users/Index'?`). Names must match exactly, as in the client's
resolver, even on case-insensitive file systems. `o.Pages.Extensions` lists the file types of page files. The default is `.tsx`,
`.jsx`, `.ts`, `.js`, `.vue` and `.svelte`. Outside Development there is no check and no file access.

---

## Testing

The `Inertia.Net.Testing` package has Laravel-style fluent assertions over Inertia responses and `HttpClient` helpers. It parses both kinds of Inertia response: JSON responses (`X-Inertia: true`) and the first-visit HTML `<script data-page>`. It also decodes `$bigint` values. A failed assertion throws `InertiaAssertionException` with a readable message, so the package works with any test framework. The example test below sends an Inertia request and asserts on the page that comes back.

```csharp
using System.Net;
using System.Net.Http.Json;
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

The helpers do the following:

- `InertiaGetAsync(url, version?, configure?)` and `InertiaRequestAsync(method, url, content?, version?, configure?)` send an Inertia request.
- `GetInertiaPageAsync()` returns the parsed `InertiaPage`. It also works for full-HTML first visits.
- `AssertInertiaAsync(assert)` parses the response, runs the assertions and returns the page.

The response helpers also parse Inertia pages that have a non-2xx status. Use `.Status(404)` in an assertion to check the HTTP status of an error page.

**Page files:** an assertion can check that the component has a page file. Construct `AssertableInertia(page, options)` with
`InertiaTestingOptions`. In the options, set `EnsurePagesExist = true` and add your page folders to `PageDirectories`.
`Component(name, false)` skips the check, and `true` forces it. Default extensions include React, Vue and Svelte files.
Matching is case-sensitive.

**Large integers:** when the page sets `preserveBigIntegers`, `Where` and `HasFlash` compare markers as integers.
This includes nested values. `InertiaPage.Raw` is not changed.

### Assertions

This section lists the assertions of `AssertableInertia`. Assertions on the page:

- `Component`, `Url`, `Version`, `StatusCode`
- `HasFlash(key)`, `HasFlash(key, value)`, `MissingFlash(key)`
- `HasDeferred(path)`, `HasDeferred(group, path)`
- `HasLive(path)`, `HasLive(path, "private:orders.1", "OrderUpdated")`. Write the channel as `"{type}:{name}"`.
- `HasHead("title", "Users - App")` for [head tags](#head-tags). Head tags are keyed like their `data-inertia` attribute.

For a `WebApplicationFactory` test host, pin `o.Version` and set `o.Ssr.Mode = SsrMode.Disabled` with `PostConfigure<InertiaOptions>`.
The [package README](src/Inertia.Net.Testing/README.md#test-host-recipe) has the recipe.

Assertions on props and nested scopes. Keys can be dot paths and can contain array indexes: `Has(key)`, `Has(key, count)`, `Has(key, scope)`, `Has(key, count, firstItemScope)`, `HasAll`, `HasAny`, `Missing`, `MissingAll`, `Where(key, value)`, `Where(key, predicate)`, `WhereNot`, `WhereNull`, `WhereNotNull`, `WhereType(key, "string|null")`, `WhereAllType`, `WhereContains(key, ...)`, `WhereRoute(key, url, method = "get")`, `Count`, `CountBetween`, `Each(scope)`, `First(scope)`, `Dump()`.

**Strict interaction tracking.** As in Laravel, a nested scope fails when it contains properties you did not assert, unless you end it with `Etc()`. The root scope is not checked automatically. Call `.Interacted()` at the end to require that every top-level prop was asserted.

### Partial reloads and deferred props

An `InertiaPage` can replay its request, so a test can load deferred props, scroll pages and partial reloads. The replay sends the right headers (`X-Inertia-Partial-Component`, the page's `url` and `version`):

```csharp
var page = await (await _client.InertiaGetAsync("/dashboard")).GetInertiaPageAsync();

new AssertableInertia(page).HasDeferred("sidebar", "stats").Missing("stats");

var loaded = await page.LoadDeferredPropsAsync(_client, "sidebar");     // all deferred groups when none are named
new AssertableInertia(loaded).Has("stats");

new AssertableInertia(page).HasScroll("posts");
var next = await page.LoadNextPageAsync(_client, "posts");
var previous = await next.LoadPreviousPageAsync(_client, "posts");

var only = await page.ReloadOnlyAsync(_client, "users");                // also asserts that "users" is in the result
var except = await page.ReloadExceptAsync(_client, "users");            // also asserts that "users" is missing
```

`HasScroll(path)` asserts that the prop has scroll metadata. `HasScroll(path, currentPage, nextPage)` also checks the page values. The page helpers use the scroll metadata and send the matching append or prepend intent.

Other `InertiaPage` members: `Props`, `Raw`, `Prop(path)`, `HasProp(path)`, `PropBigInteger(path)`, `DeferredProps`, `MergeProps`, `PrependProps`, `DeepMergeProps`, `MatchPropsOn`, `RescuedProps`, `SharedProps`, `ScrollProps`, `OnceProps`, `LiveProps`, `Flash`, `EncryptHistory`, `ClearHistory`, `PreserveFragment`, `PreserveBigIntegers`.

To test a form post that redirects back with flash data or errors, use a client that keeps cookies, because flash data and errors travel in a cookie across the redirect. The `WebApplicationFactory` client keeps cookies by default. Disable `AllowAutoRedirect` to assert the redirect itself. Load the form first to get its XSRF token, and keep the paired antiforgery cookie too. This example uses the contact form from the samples:

```csharp
_client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
using var form = await _client.GetAsync("/contacts/create");
using var submitted = await _client.InertiaRequestAsync(HttpMethod.Post, "/contacts",
    JsonContent.Create(new { name = "", email = "" }), configure: request =>
    {
        request.Headers.Referrer = new Uri(_client.BaseAddress!, "/contacts/create");
        request.WithXsrfToken(form);
    });
Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
using var followed = await _client.GetAsync("/contacts/create");
await followed.AssertInertiaAsync(p => p.HasErrors().HasError("name").HasError("email"));
```

`GetXsrfToken()` reads and URL-decodes the token. `WithXsrfToken()` accepts a token or a page response, and optional custom cookie and header names. `HasErrors(bag?)`, `HasError(field, message?, bag?)` and `HasErrorBag(bag)` accept client keys such as `items.0.email`, and messages that are a string or an array. The default bag is exposed flat, as `errors.email`. A named bag is nested, for example `errors.createUser.email`. Use the same cookie-keeping client for both requests. Add `public partial class Program;` to your app when it uses top-level statements.

---

## Performance

This section reports benchmark results for Inertia.Net and gives performance guidance. The full write-ups are in three places:

- the [framework comparison](https://inertia-dotnet.temtem.dev/docs/framework-comparison) on the docs site
- [benchmarks/frameworks](benchmarks/frameworks/README.md), for the method and raw results of the framework comparison
- [tests/Inertia.Net.Benchmarks/RESULTS.md](tests/Inertia.Net.Benchmarks/RESULTS.md), for the adapter's own measurements

**Against other full-stack frameworks.** The same todo app, with the same React pages, Postgres data and Redis cache, was built in
Inertia.Net, Next.js, TanStack Start, Nuxt and Laravel (Octane on FrankenPHP). Each app ran in Docker with 2 CPUs and 2 GB on GitHub
runners. The table shows requests per second in SSR mode with 16 connections (run of 2026-11-16,
[results](benchmarks/frameworks/results/2026-11-16-v3.md)). Its last row shows memory at rest instead:

| Route | Inertia.Net JIT | Inertia.Net AOT | TanStack Start | Nuxt | Next.js | Laravel |
|---|---:|---:|---:|---:|---:|---:|
| `/todos?perPage=25`, first visit | 1,764 | **3,905** | 1,294 | 745 | 364 | 337 |
| `/todos?perPage=25`, client-side navigation | 6,155 | **9,789** | 3,962 | 5,498 | 1,512 | 550 |
| New todo, valid post | 2,810 | **2,956** | 2,464 | 1,996 | 957 | 517 |
| `/compute?n=20000` (CPU-heavy) | **1,212** | 1,033 | 227 | 240 | 207 | 81 |
| Memory at rest, SPA / SSR | 27 / 60 MB | **17** / 46 MB | 28 / 29 MB | 138 / 139 MB | 72 / 76 MB | 98 / 154 MB |

**With every framework's own caching on.** This measurement turns on the caching that each framework provides:

- Inertia.Net: the [SSR render cache](#6-response-cache) and [cached props](#cached-props)
- Next.js: `'use cache'`
- TanStack Start and Nuxt: Nitro route rules
- Laravel: spatie/laravel-responsecache

Results for first visits to the 25-row todo list ([results](benchmarks/frameworks/results/2026-11-16-all-tuned.md)): Inertia.Net
**34,020** req/s, Nuxt 15,481, TanStack Start 9,543, Laravel 1,201 and Next.js 439. Inertia.Net still runs the handler and loads the
props on every request. The render cache reuses Node's HTML only for identical page JSON, and only props marked `.Cache(...)` are
served from cache.

**Overhead over plain ASP.NET Core.** The hosting probe serves the same data in two ways: as a plain `Results.Json` API and as an
Inertia visit. It ran on a GitHub runner with the server on 2 vCPUs (1.9.2,
[details](tests/Inertia.Net.Benchmarks/RESULTS.md#192-rendering-through-the-apps-json-context-and-the-spa-vs-ssr-suite--2026-11-16)):

| Build | Inertia visit as a share of the plain JSON API's req/s | Extra server CPU per request |
|---|---:|---:|
| JIT, server GC | 88% (86,565 vs 98,183) | about 3 µs |
| Native AOT, server GC | 84% (77,134 vs 91,409) | about 4 µs |

Native AOT halves memory: about 33 MB at rest against 74 MB for JIT in the same probe. Native AOT also starts about six times faster.
JIT serves more requests per second.

**Server-side rendering.** Rendering in Node is where SSR time goes. A 25-row page served 64,640 req/s as an SPA first load and
3,176 req/s server-rendered. Calling Node's `/render` directly was only about 9% faster than going through Inertia.Net. Inertia
visits (JSON) never call Node, so SSR only costs first loads. With the render cache on, a repeated anonymous page served
97,035 req/s. When SSR is the bottleneck, run several renderers (`n.Instances`). Each Node process takes about 80 MB at rest.

**Register your JSON context first.** If the app has a source-generated `JsonSerializerContext`, insert it first in
`TypeInfoResolverChain`. Inertia.Net then writes plain values (records, lists, strings, numbers) through the generated code. In the
hosting probe, this cut CPU per Inertia request by about 16% on JIT and 20% on Native AOT. `dotnet new inertia` already does this:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
```

Inertia.Net uses the generated code only when the output is identical. It falls back to the general path in any of these cases:

- The context is not first.
- The context's settings differ from the app's JSON options.
- Custom converters or `PreserveBigIntegers` are in use.
- A property name has characters other than letters, digits, `_`, `-`, `.` and `$`.

**Memory planning.** The [performance page](https://inertia-dotnet.temtem.dev/docs/performance) lists measured memory at rest and under
load for JIT and Native AOT apps and for the Node or Bun SSR process, with container sizing advice.

To measure your build, run `dotnet run -c Release -- --filter '*'` from `tests/Inertia.Net.Benchmarks`. Targeted probes are available
with `--render-probe`, `--ssr-probe`, `--mvc-probe`, `--payload-probe`, `--cache-probe` and `--feature-probe`. `eng/hosting-probe.sh`
runs the hosting comparison, and `benchmarks/frameworks/run.sh` runs the framework comparison.

**Payload size.** The page JSON is fully buffered before the first byte is sent, so a failing loader never produces a half-written 200.
Each concurrent request therefore holds its whole page in memory. The buffer is pooled and sized from the component's recent pages.
Buffers over 1 MB go back to the runtime after the request instead of staying in the pool. Large JSON pages are sent in 32 KB pieces.
Keep pages well under a megabyte. For large data, paginate (`Scroll`), defer (`Defer`) or load on demand (`Optional`).
`Ssr.MaxPageBytes` keeps larger pages off the SSR server, and `Ssr.MaxResponseBytes` limits the size of the SSR server's answer.
Neither option limits the props.

Design notes:

- **Single pass.** One recursive walk filters props, runs loaders, writes JSON to a `Utf8JsonWriter` and collects the metadata. There is no intermediate object tree.
- **Pooled buffers.** The page is written to a pooled `IBufferWriter<byte>`, and the path tracker is a pooled `char[]`. A path becomes a string only when metadata needs it. The buffer also means a failing loader never produces a half-written 200.
- **Version check before the handler.** A stale client costs one header comparison and a 409.
- **No per-request mutable singletons.** Per-request state lives in an `InertiaFeature` on `HttpContext.Features`, so concurrent requests cannot leak history or flash settings into one another.
- **Plain DTOs take the fast path.** A POCO, list or typed dictionary is walked only when its type can hold `InertiaProp` values, which means it has `InertiaProp`, `object` or dictionary members or elements. This check is cached per type. Everything else is one `Serialize` call. An object of a walked type is also one `Serialize` call when its `object` members hold only plain values at render time, unless a partial reload filters inside it.
- **UTF-8 throughout.** SSR output and the MVC tag helpers write UTF-8 bytes; nothing large is materialized as a .NET string.
- **Cached assets.** The Vite manifest is parsed once, the root template is parsed once into pre-encoded UTF-8 segments, and the production tags are pre-rendered per entry set and path base. The version is a precomputed string compared ordinally. A manifest, template or SSR bundle that is missing at startup is re-checked at most once per second, so files deployed after the app starts are picked up without a restart.
- **Sync loaders stay sync.** `Func<T>` loaders are not wrapped in closures.

Most per-request allocation in a real handler is the props you build, not the render.

---

## Configuration reference

The tables in this section list the options of each options class, their defaults and what they do.

### `AddInertia(o => ...)`: `InertiaOptions`

| Option | Default | Description |
|---|---|---|
| `RootView` | `"app.html"` | Root template, relative to the content root. With `UseRazorRootView` it is a Razor view name. Paths outside the content root are refused. Never take this value from the request. |
| `RootElementId` | `"app"` | Id of the root element and the `data-page` script. |
| `Version` | `null` | Asset version. `null` means the hash of the Vite manifest, or `""` when there is no manifest. |
| `VersionResolver` | `null` | `Func<HttpContext, string?>` per-request version. Wins over `Version` when non-null. |
| `ComponentTransformer` | `null` | `Func<string, string?>` that changes a component name before rendering and page-file checks. Null keeps the name. |
| `EncryptHistory` | `false` | Encrypt browser history state of every page. |
| `WithAllErrors` | `false` | Send every validation message per field instead of the first. |
| `PreserveBigIntegers` | `false` | Emit integers beyond ±(2^53−1) as `{"$bigint":"..."}` and set `preserveBigIntegers`. |
| `ExposeSharedPropKeys` | `true` | List top-level shared keys in `sharedProps`. |
| `CspNonce` | `null` | `Func<HttpContext, string?>`: the request's CSP nonce, written on every script and stylesheet tag. |
| `ConvertExternalRedirects` | `true` | Turn an Inertia request's redirect to another origin into `409` + `X-Inertia-Location`. |
| `UrlResolver` | `null` | `Func<HttpContext, string>`: the page `url`. |
| `Pages.Directory` / `Pages.Extensions` | `null` / `.tsx .jsx .ts .js .vue .svelte` | Development check that a rendered component has a page file. |
| `TypeScript.OutputPath`, `.RoutesOutputPath`, `.Page<T>(component)`, `.Page<TPage>()`, `.PagesFrom(assembly)`, `AddGeneratedPages()`, `.SharedProps<T>()`, `.FlashData<T>()`, `.LiveEvents(...)` | `null` | TypeScript declarations and typed routes, written in Development, with `--inertia-types`, or on build with `InertiaGenerateTypesOnBuild`. |
| `Share(key, value)` / `Share(key, Func<HttpContext, object?>)` / `Share<T>(key, Func<HttpContext, T>)` | none | Shared props. |
| `Head.PropName` / `Head.TitleTemplate` | `"head"` / `null` | The prop that carries [server head tags](#head-tags), and the title format. |

`ComponentTransformer` affects runtime rendering only. If you use it, register the final client component names in TypeScript
configuration too, because build-time generation cannot run an arbitrary runtime callback.

### `o.Pagination`: `PaginationOptions`

| Option | Default | Description |
|---|---|---|
| `DefaultPerPage` | `20` | Default page size. |
| `MaxPerPage` | `100` | Maximum page size. Must be below `int.MaxValue` to allow over-fetching. The default page size must be between 1 and this value. |
| `MaxPage` | `null` | The last numbered page that a bound `PageRequest` may fetch. A page beyond it is empty and runs no query or count. `null` means no ceiling. |
| `PageName` | `"page"` | Numbered-page query parameter. |
| `PerPageName` | `"perPage"` | Page-size query parameter. |
| `CursorName` | `"cursor"` | Cursor query parameter. |

### `o.Vite`: `ViteOptions`

| Option | Default | Description |
|---|---|---|
| `PublicDirectory` | `"wwwroot"` | Web root containing the build directory and hot file. |
| `BuildDirectory` | `"build"` | Vite output directory under `PublicDirectory`; also the URL prefix. |
| `ManifestPath` | `null` | Manifest path relative to the content root. `null` means `{PublicDirectory}/{BuildDirectory}/.vite/manifest.json` first, then `manifest.json`. |
| `HotFilePath` | `null` | Hot file relative to the content root. `null` means `{PublicDirectory}/hot`. Read in Development only. |
| `DevServerUrl` | `null` | Dev server origin, for example `http://localhost:5173`. When set, dev tags always render and the hot file is ignored. |
| `AssetBaseUrl` | `null` | Replaces `{PathBase}/{BuildDirectory}` as the URL prefix of built assets (CDN). |
| `Prefetch` | `None` | `Waterfall` or `Aggressive`: prefetch the code-split chunks after load. |
| `PrefetchConcurrency` | `3` | Number of chunks that `Waterfall` downloads at a time. |

### `o.Ssr`: `SsrOptions`

`OnRenderFailed` defaults to null. Set a thread-safe `Action<SsrRenderFailed>` to send final failed-render diagnostics to monitoring.

| Option | Default | Description |
|---|---|---|
| `Mode` | `Disabled` | `Disabled`, `Preferred` (fall back to client rendering on failure) or `Required` (throw `InertiaSsrException`). `Disabled` cannot be overridden per response. |
| `Urls` | empty (`http://127.0.0.1:13714`) | SSR server base URLs (`/render`). Several URLs spread the renders. Not used with `UseNodeProcess()`. |
| `ConnectTimeout` | `5 s` | Timeout for establishing a connection, independent of the render deadline. Must be positive or `Timeout.InfiniteTimeSpan`. |
| `Timeout` | `5 s` | Total deadline per render: the wait for a slot plus the HTTP call. `Timeout.InfiniteTimeSpan` disables it. |
| `MaxResponseBytes` | `16 MiB` | Limit on actual response bytes, including errors and chunked responses. Oversized responses fall back, or throw with type `response-too-large`. |
| `MaxPageBytes` | `2 MiB` | A page whose JSON is larger renders on the client, without a call to the SSR server (type `page-too-large`). |
| `BundlePath` | `null` | Skip SSR while this file is missing, unless the Vite dev server runs. A missing bundle is re-checked at most once per second. In production, a bundle that is present is not checked again. |
| `ExcludePaths` | empty | Request paths that are never rendered on the server, unless the response calls `WithSsr`. An entry is an exact path, or ends with `*` to match a prefix. Each entry must start with `/`. |
| `Concurrency.MaxActive` | `8` | Maximum renders in flight per server from this app instance. `0` means no limit. |
| `Concurrency.MaxQueued` | `32` | Maximum renders waiting for a slot per server. Further renders fall back at once. |
| `Concurrency.QueueTimeout` | `250 ms` | How long a render waits for a slot. |
| `CircuitBreaker.Enabled` | `true` | Pause servers that keep failing. |
| `CircuitBreaker.FailureThreshold` | `5` | Consecutive connection, timeout, HTTP or malformed-response failures that open a server's circuit. |
| `CircuitBreaker.BreakDuration` | `10 s` | How long an open circuit skips the server before one render probes it. |
| `Cache.Duration` | `Zero` (off) | Reuse a render for this long when the page JSON is the same. Per response: `WithSsrCache(duration)`. |
| `Cache.StaleFor` | `Zero` | After `Cache.Duration`, keep serving the cached render for this long, while the page renders again after the response. Per response: `WithSsrCache(duration, staleFor)`. |
| `Cache.MaxBytes` | `64 MiB` | Size of the default store's render cache; least recently used renders are evicted. |
| `UseNodeProcess(configure?)` | off | Start and supervise Node SSR processes with the app. Sets `Mode = Preferred` when the mode is `Disabled`. The options are in the next table. |

To set the SSR mode for one response, use `Render(...).WithSsr(SsrMode)` or `.WithoutSsr()`.

### `o.Ssr.UseNodeProcess(n => ...)`: `SsrNodeProcessOptions`

| Option | Default | Description |
|---|---|---|
| `BundlePath` | `Ssr.BundlePath`, else probed | Bundle to run, relative to the content root. Probes `ssr/ssr.{js,mjs}`, `bootstrap/ssr/ssr.{js,mjs}`, `dist/ssr.{js,mjs}`. |
| `Executable` | `"node"` | Node executable (path, or name on `PATH`). |
| `Arguments` | empty | Node options, passed before the bundle path. |
| `Environment` | empty | Extra environment variables. The process inherits the app's environment, and `INERTIA_SSR_PORT` and `NODE_ENV=production` are added. |
| `WorkingDirectory` | content root | Process working directory. |
| `StartupTimeout` | `10 s` | How long startup waits for every instance's `/health`. After that, startup gives a warning, or `InertiaSsrException` in `Required` mode. |
| `RestartOnExit` | `true` | Restart after an unexpected exit or a failed liveness check. The delay starts at 1 s and doubles up to 30 s. |
| `Instances` | `1` | Node processes to run (1 to 64). Instance `i` listens on `127.0.0.1:{BasePort + i}` and gets that port in `INERTIA_SSR_PORT`. |
| `BasePort` | `13714` | Port of the first instance. |
| `LivenessInterval` | `5 s` | How often each running instance is checked with `GET /health` (2 s timeout). |
| `LivenessFailureThreshold` | `3` | Failed checks in a row after which the instance is killed and restarted. |
| `AllowNetworkExposure` | `false` | Keep an instance that also answers on a non-loopback address (with a warning) instead of stopping it. |

### `o.Props`: `InertiaPropsOptions`

| Option | Default | Description |
|---|---|---|
| `MaxConcurrency` | `4` | Loaders marked `.Parallel()` that run at once per render. Must be positive. |
| `CacheMaxBytes` | `null` | Size of the default store's cached props. When set, cached props go in a private cache and the least recently used are evicted. `null` keeps them in the app's `IMemoryCache` without a size limit of their own. |

### `o.State`: `InertiaStateOptions`

| Option | Default | Description |
|---|---|---|
| `CookieName` | `".Inertia.State"` (`".Inertia.State.{mount}"` for a named mount) | Cookie name (cookie store) or session key (session store). |
| `RememberPreviousUrl` | `false` | Remember successful page GET URLs for `Back()` when Referer is unavailable. |
| `PreviousUrlCookieName` | `".Inertia.Previous"` (`".Inertia.Previous.{mount}"` for a named mount) | Name of the plain previous-URL cookie, used with either state store. Must be non-empty and different from `CookieName`. |
| `CookieSecurePolicy` | `SameAsRequest` | When the state and previous-URL cookies are `Secure`. Use `Always` behind a TLS-terminating proxy that does not forward the scheme. |
| `UseSession()` | off | Use `ISession` instead of the encrypted cookie. The session is only loaded when the request has a session cookie. |
| `AddInertiaMvc(o => o.UseTempDataStateStore())` | off | Use MVC TempData (cookie or session provider) instead of the encrypted cookie. Inertia.Net.Mvc package. |

### `AddInertiaErrorPages(o => ...)`: `InertiaErrorPagesOptions`

| Option | Default | Description |
|---|---|---|
| `Component` | `"Error"` | Component for error pages without a per-status override. |
| `Components` | empty | Status-to-component overrides; keys must also be in `StatusCodes`. |
| `StatusCodes` | `[403, 404, 500, 503]` | Non-empty set of error statuses (400 to 599). Protocol `409` and antiforgery `419` responses are preserved. |
| `EnabledInDevelopment` | `false` | Render error pages in Development too. |
| `Props` | `null` | `Func<HttpContext, int, Exception?, InertiaProps>` for extra error page props. |

### Middleware, endpoint and result API

| API | Description |
|---|---|
| `services.AddInertia(configure?)` | Registers services and options. |
| `services.AddInertiaErrorPages(configure?)` / `app.UseInertiaErrorPages(useExceptionHandler: true)` | Registers error page handling and wraps the pipeline before `UseInertia()`. Set the flag to `false` when using your own `UseExceptionHandler()`. |
| `app.UseInertia()` | The protocol middleware. Call it before the endpoints, and after `UseSession()` when the session store is used. |
| `services.AddInertia(name, configure)` / `app.UseInertia(name)` | A named [mount](#multiple-apps-and-libraries): its own options, version, state cookie, root view (keyed `IInertiaRootView`) and SSR, selected in a `Map` branch. `httpContext.Inertia().Mount` is the request's mount (null for the default). |
| `services.AddInertiaErrorPages(name, configure)` | Error pages of a named mount. A mount without them leaves its errors to the pipeline. |
| `app.UseInertiaAntiforgery(configure?)` | Issues the `XSRF-TOKEN` cookie and validates every unsafe request (`419` on failure). Options: `CookieName` (`"XSRF-TOKEN"`), `SecurePolicy` (`SameAsRequest`), `ShouldValidate`, `OnRejected`, `CookieRefresh` (`Always` by default, or `Lazy`). |
| `app.MapInertia(pattern, component, props?)` | GET and HEAD endpoint that renders the component. |
| `services.AddInertiaDevTools(configure?)` / `app.MapInertiaDevTools()` | DevTools recorder and its read API. `Enabled` (default: Development only), `Authorize`, `MaxEntriesPerTab` 100, `MaxEntries` 1000, `MaxAge` 30 min, `MaxBodyBytes` 64 KiB, `MaxResponseBytes` 1 MiB, `MaxTotalBytes` 64 MiB, `RedactedKeys`. |
| `app.UseInertiaAntiforgeryCookie(cookieName = "XSRF-TOKEN")` | Only writes the antiforgery request token to a script-readable cookie; validation is up to you. |
| `endpoint.WithInertiaValidation()` | Minimal API: validation failures redirect back with errors. Works on an endpoint or a group. |
| `endpoint.WithInertiaPrecognition()` | Minimal API: `WithInertiaValidation()` plus Precognition. |
| `healthChecks.AddInertiaSsr(name = "inertia-ssr", failureStatus?, tags?, timeout?)` | SSR health check. It reads the gateway's state and makes no call to the servers. |
| `healthChecks.AddInertiaSsrForMount(mount, name?, failureStatus?, tags?, timeout?)` | SSR health check of a named mount (default name `inertia-ssr-{mount}`). |
| `mvc.AddInertiaMvc(o => o.UseRazorRootView("App"))` | MVC filter, optional Razor root view, `UseTempDataStateStore()`, and `ComponentResolver` (`Func<ControllerContext, string>`) for `this.InertiaView(props)`. |
| `cookieOptions.UseInertiaRedirects()` | Cookie authentication: login and access-denied redirects that Inertia visits follow (see [Authentication](#authentication-and-rate-limiting)). |
| `InertiaRateLimiting.OnRejected(flashKey = "error", message = ...)` | A rate limiter `OnRejected` that redirects back with a flash message instead of a bare `429`. |
| `services.AddInertiaLive().AddSignalR(o => ...)` / `app.MapInertiaLiveHub(pattern = "/inertia-live")` | [Live props over SignalR](#signalr). Options: `AuthorizePublicChannels` (false), `MaxSubscriptionsPerConnection` (64), `MaxChannelNameLength` (200), `AllowedOrigins`, `Authorize<T>()`. |
| `services.AddInertiaRedisCache(o => ...)` | [Redis store](#redis) for cached props and the SSR render cache: `Configuration`, `Database`, `KeyPrefix`. |
| `config.ConfigureInertia(configure?)` | FastEndpoints: validation redirect and Precognition (`app.UseFastEndpoints(c => ...)`). |
| `Render(component, props?)` / `Render<TPage>(page)` | `InertiaResult`: `.WithHead(h => ...)`, `.WithStatusCode(int)`, `.WithRootView(string)`, `.WithViewData(key, value)`, `.WithSsr(SsrMode)`, `.WithoutSsr()`, `.WithSsrCache(TimeSpan[, staleFor])`. |
| `Location(url)` | `InertiaLocationResult`. |
| `Back(fallback = "~/")` | `InertiaBackResult`: `.WithErrors(errors, bag = "default")`, `.WithFlash(key, value)`. |
| `httpContext.Inertia()` | `InertiaFeature`: `Share`, `Flash`, `ForgetFlashed`, `WithErrors`, `ClearHistory`, `EncryptHistory(bool = true)`, `PreserveFragment`, `Head`, `PreviousUrl`, `Mount`. |
| `Send.InertiaAsync(component, props?)`, `Send.InertiaAsync(page)`, `Send.InertiaLocationAsync(url)`, `Send.InertiaBackAsync(fallback = "~/")` | FastEndpoints `Send` extensions. |

Extension points: `IInertiaRootView`, `IInertiaStateStore`, `IInertiaCache`, `IInertiaCacheStore`, `IInertiaLive`, `IInertiaPage`, `ILiveChannelAuthorizer`, `ViteAssets`, `InertiaRequest` (the parsed headers), `InertiaHeaders` (header name constants), `InertiaSsrException`, `InertiaSsr.HttpClientName` (the SSR HTTP client), `InertiaDiagnostics` (meter and activity source names).

---

## Migrating from other .NET adapters

This section maps the API of other .NET Inertia adapters to the Inertia.Net API. The Inertia.Net API is close to the Laravel adapter's names. The table maps the typical InertiaCore-style API (kapi2289/InertiaCore and its forks). Check the names against the adapter version you use.

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
| `ModelState` errors in session | MVC: `AddInertiaMvc()`; Minimal API: `.WithInertiaValidation()`; FastEndpoints: `c.ConfigureInertia()` |
| `Version` option or manifest hashing per request | `o.Version`, `o.VersionResolver`, or the cached manifest hash |
| SSR options (`UseSsr`, `SsrUrl`) | `o.Ssr.Mode`, `o.Ssr.Urls`, `o.Ssr.ExcludePaths`, `Render(...).WithSsr(...)` |

Behavior differences to check when you migrate:

- Inertia.Net supports v3 only and does not work with v2 clients. Upgrade the client packages to v3. The first page is the `<script data-page="app">` element, not a `data-page` attribute on the root div.
- Props are resolved without reflection. Anonymous types need reflection-enabled serialization, and AOT apps register a `JsonSerializerContext`.
- Props serialize with the HTTP JSON options (camelCase by default), not Newtonsoft.
- The version check runs before the handler.
- Flash, errors and history flags use an encrypted cookie unless you opt into the session store.

---

## Troubleshooting and FAQ

Each entry in this section starts with a symptom or a question in bold. The cause and the fix follow.

**My 404 shows the Inertia modal in production.**
Register `AddInertiaErrorPages()` and call `UseInertiaErrorPages()` before `UseInertia()`. Create the configured error components. See [Error pages](#error-pages).

**`Inertia.Render(...)` does not compile / "'Inertia' is a namespace".**
Call `Render(...)` unqualified, that is, with no class name in front of it. The namespace `Inertia.Net` shadows the class name outside the `Inertia.Net` namespace. The global usings come from the package's `buildTransitive` props and need `<ImplicitUsings>enable</ImplicitUsings>`. Without implicit usings, or with `InertiaNetImplicitUsings` set to `false`, add `using static Inertia.Net.Inertia;` to the file. See the callout under [Installation](#installation). A class library that references `Inertia.Net` gets the same global usings.

**Generated code fails with "'Inertia' is a type" (e.g. FastEndpoints' source generator).**
Inside a namespace that starts with `Inertia.Net.`, the name `Inertia` binds to the `Inertia.Net.Inertia` class. Source generators that emit non-`global::` names starting with `Inertia` then break. Don't put your app's code under an `Inertia.Net.*` namespace.

**`TypeLoadException: Could not load type 'Microsoft.Extensions.Validation.IValidatableInfo'` (or `ValidatableTypeInfo`) on .NET 11.**
Upgrade every Inertia.Net package to 1.8.0 or later and target `net11.0`. Do not run a `net10.0` build on the .NET 11 runtime. See
[.NET 10 and .NET 11](#net-10-and-net-11).

**`Vite manifest not found. Looked in: ...`**
Do one of these:

- Run your package manager's build script.
- In Development, start the dev server so that the hot file exists.
- Set `o.Vite.ManifestPath` or `o.Vite.BuildDirectory`.

Check that Vite writes `.vite/manifest.json` (`build.manifest: true`, Vite 5 and later) to `wwwroot/build`. Also check that the build output is part of what you deploy.

**`Vite entry 'resources/js/app.tsx' was not found in the manifest`**
The `@vite("...")` argument must equal the manifest key, which is the path relative to the Vite root. The error lists the available entries.

**Form submissions fail with `419`.**
`UseInertiaAntiforgery()` rejected a request without a valid `X-XSRF-TOKEN`. Check these points:

- The page was loaded through the middleware, which sets the `XSRF-TOKEN` cookie.
- The cookie reaches the browser. `Secure` on plain HTTP is dropped. See [Deploying behind a proxy](#deploying-behind-a-proxy).
- All instances share the Data Protection key ring.
- `UseAuthentication()` runs before the antiforgery middleware.

A sign-in or sign-out changes the token. After one, reload the page, or return `Back()` from `OnRejected`. Exempt webhooks and
token-authenticated APIs with `.DisableAntiforgery()` or `ShouldValidate`.

**`The Inertia page component '...' was not found in '...'` (Development).**
`o.Pages.Directory` is set and the component passed to `Render(...)` has no page file there. Names match exactly, as the client's page
resolver does, even on a case-insensitive file system. The message suggests the closest page. Fix the name, or check
`Pages.Extensions` when your pages use another extension. The check never runs outside Development.

**A redirect to another site turns into a `409`.**
This is the `ConvertExternalRedirects` option, which is on by default. An Inertia request cannot follow a redirect to another origin,
so it gets `409` + `X-Inertia-Location` and the client does a full visit. If this happens for your own host, the app is behind a proxy
that does not forward the scheme (see [Deploying behind a proxy](#deploying-behind-a-proxy)), or use relative redirects.
`o.ConvertExternalRedirects = false` disables this conversion.

**`The Inertia root view '...' resolves outside the content root`.**
`RootView` and `WithRootView(...)` take a path relative to the content root. Absolute paths and `..` are refused. Never derive these paths from request data.

**The page keeps reloading, or a request loops with `409`.**
A 409 means the asset version differs between the client and the server. A loop means the two versions never become equal. There are two usual causes:

- The version changes on every request. For example, a `VersionResolver` returns a new value each time, or a file watcher regenerates the manifest.
- Different instances behind a load balancer serve different builds.

Use a stable version string. In Development the manifest hash changes after each `vite build`, which is expected once.

**The dev server tags do not appear.**
The hot file is read only when `ASPNETCORE_ENVIRONMENT=Development`. Check that `wwwroot/hot` exists and contains `http://localhost:5173`, or set `o.Vite.DevServerUrl`. Also set `base: '/'` for the dev server in `vite.config` (the `command === 'build'` check in the example).

**`The Inertia root template '...app.html' was not found` or a missing `@inertia` token.**
`RootView` is relative to the **content root**. Copy `app.html` to the output and publish directories (`CopyToOutputDirectory`, `CopyToPublishDirectory`). The template must contain `@inertia`.

**Flash data or errors disappear, or a warning says the cookie is over 4 KB.**
Browsers limit the state cookie to about 4 KB. Flash less data, or use `o.State.UseSession()`. If the app runs on several instances, share Data Protection keys. If the browser never sends the cookie back, check the cookie `Secure` flag on HTTP behind a proxy that does not forward `X-Forwarded-Proto` (see [Deploying behind a proxy](#deploying-behind-a-proxy)).

**Validation errors do not show up (Minimal API).**
Use `.WithInertiaValidation()` on the endpoint. Without it, the built-in `AddValidation()` filter returns a `400` problem details response that the Inertia client cannot use. Make sure the model types are `public` (see [Native AOT](#native-aot)). The request must be an Inertia request and not a GET.

**Validation keys do not match my field names.**
Keys use JSON member aliases where metadata provides them and the naming policy for other members (`Address.Street` becomes `address.street`). Minimal API and MVC use the page serializer’s HTTP JSON options; FastEndpoints uses its binding serializer policy. Keep client field names aligned with those settings.

**`InvalidOperationException: InertiaProp values can only be serialized by Inertia.Net`.**
An `InertiaProp` is in a place that Inertia.Net does not walk. Such a place is one of these:

- a `Flash(...)` value
- a read-only dictionary type that does not implement `IDictionary`
- a type with a custom converter

Inertia.Net walks lists, typed dictionaries, `object` members and `IDictionary<string, object?>`. A typed dictionary is a `Dictionary<TKey, TValue>` with any key type, and its keys are written as System.Text.Json writes them. To fix the error, put props in `InertiaProps`, or declare the member as `InertiaProp` (or `object`) on the typed page object.

**Serialization fails under Native AOT: "JsonTypeInfo metadata ... was not provided".**
Add the type to your `JsonSerializerContext` and chain it with `ConfigureHttpJsonOptions` (see [Native AOT](#native-aot)). Setting `JsonSerializerIsReflectionEnabledByDefault=false` in regular builds finds these errors early.

**An error says `Stopped the SSR server: it also accepts connections on ...`.**
The managed Node SSR server listens beyond loopback, so Inertia.Net stopped it. The `@inertiajs/vite` default host is `0.0.0.0`. Rebuild with `inertia({ ssr: { host: '127.0.0.1' } })`, or listen on `127.0.0.1` in your entry. `AllowNetworkExposure = true` keeps the server running with a warning.

**SSR does not run and there is no error.**
SSR falls back to client rendering on any failure. Look for the warnings `Inertia SSR failed (...)` and `Inertia SSR skipped (...)` in the logs. The first warning includes the server's hint. Then check these points:

- `o.Ssr.Mode` and `Urls` are set as you expect.
- The Node server is running (`GET /health`). With `UseNodeProcess()`, look for `Inertia.Net.Ssr` log lines.
- `BundlePath` points at the built file.
- The path is not in `ExcludePaths`.
- The page is under `MaxPageBytes`.

Set `Mode = SsrMode.Required` in Development to surface the failure.

**`Inertia SSR skipped (circuit-open)` or `(overloaded)`.**
`circuit-open` means the server failed `CircuitBreaker.FailureThreshold` times in a row and is skipped for `BreakDuration`. The earlier `Inertia SSR failed` warnings say why. `overloaded` means more renders arrived than `Concurrency.MaxActive` plus `MaxQueued`, or one render waited longer than `QueueTimeout`. Add servers or instances, or raise the limits if the server has spare capacity.

**Several managed instances never become healthy.**
Every instance must listen on its own port, so the SSR entry has to read `process.env.INERTIA_SSR_PORT` (see [Let the app run the Node server](#let-the-app-run-the-node-server)). A bundle built only by `@inertiajs/vite` listens on the port that was fixed at build time.

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

To build and test the repository, run:

```sh
dotnet build Inertia.Net.slnx -c Release -warnaserror
dotnet test --solution Inertia.Net.slnx -c Release --no-build
bash eng/aot-smoke.sh                    # Native AOT publish + protocol smoke test (Linux, clang + zlib)
```

- The repository builds with the .NET 11 SDK (`global.json`) and needs the .NET 10 runtime too. The libraries and test projects target
  `net10.0;net11.0`, and `dotnet test` runs both. Samples, the docs app, AotSmoke and Benchmarks stay on `net10.0`.
  `TFM=net11.0 bash eng/aot-smoke.sh` runs the Native AOT smoke test on .NET 11.
- Warnings are errors. The code is nullable-annotated and the public API is documented.
- Every protocol rule should be backed by a test in `tests/Inertia.Net.Tests` or a conformance case in `tests/Inertia.Net.IntegrationTests`. The conformance cases run against Minimal API, MVC and FastEndpoints hosts. The protocol itself is in [docs/protocol.md](docs/protocol.md), including the deliberate deviations from inertia-laravel.
- Changes are listed in [CHANGELOG.md](CHANGELOG.md). To cut a release, push a `v*` tag. The release workflow then builds, tests, packs and publishes to NuGet from the `release` environment. It publishes through trusted publishing (GitHub OIDC, no stored API key). Give that environment required reviewers, so that a pushed tag alone cannot publish.

Licensed under the [MIT license](LICENSE).
