# Migrating to Inertia.Net 1.3

Update all Inertia.Net packages to 1.3.0 together. The Inertia v3 JSON protocol and the cookie, session and TempData formats are unchanged.
The SSR options change shape, so apps that use SSR must update their configuration; apps without SSR only need to check
[dot-key props](#lazy-props-with-dot-keys-below-them). Invalid options now fail at startup with a message that names each setting.

## SSR options

```csharp
// 1.2
builder.Services.AddInertia(o =>
{
    o.Ssr.Enabled = true;
    o.Ssr.Url = "http://ssr.internal:13714";
    o.Ssr.ThrowOnError = builder.Environment.IsDevelopment();
    o.Ssr.MaxResponseBytes = null;
});

// 1.3
builder.Services.AddInertia(o =>
{
    o.Ssr.Mode = builder.Environment.IsDevelopment() ? SsrMode.Required : SsrMode.Preferred;
    o.Ssr.Urls.Add("http://ssr.internal:13714");
    o.Ssr.MaxResponseBytes = 64L * 1024 * 1024; // a limit is required; the default is 16 MiB
});
```

| 1.2 | 1.3 |
|---|---|
| `Ssr.Enabled` | `Ssr.Mode`: `Disabled` (default), `Preferred` (log and render on the client on failure), `Required` (throw `InertiaSsrException`). |
| `Ssr.ThrowOnError` | `Ssr.Mode = SsrMode.Required`, or `Render(...).WithSsr(SsrMode.Required)` for the pages that must not fall back. |
| `Ssr.Url` | `Ssr.Urls` (empty means `http://127.0.0.1:13714`). Several URLs spread the renders. |
| `Ssr.MaxResponseBytes = null` | Not allowed; set a large positive limit instead. |
| `Ssr.Timeout = TimeSpan.Zero` | Not allowed; use a positive value or `Timeout.InfiniteTimeSpan`. The timeout now also covers the wait for a render slot. |

A response can now pick its own mode: `Render(...).WithoutSsr()` renders on the client, and `WithSsr(SsrMode.Preferred)` renders on the
server even when `ExcludePaths` matches. A global `Mode = Disabled` still turns SSR off for every response. `ExcludePaths` entries must
start with `/`.

## New SSR limits that are on by default

Check these against your traffic before deploying. Each one makes a page render on the client instead of on the server, and logs
`Inertia SSR skipped (...)` at most once every 30 seconds per reason.

| Limit | Default | To restore the 1.2 behavior |
|---|---|---|
| Renders in flight per server, per app instance | `Concurrency.MaxActive = 8`, with `MaxQueued = 32` waiting up to `QueueTimeout = 250 ms` | `o.Ssr.Concurrency.MaxActive = 0` |
| Circuit breaker | Opens after `CircuitBreaker.FailureThreshold = 5` consecutive connection, timeout, HTTP or malformed-response failures, for `BreakDuration = 10 s` | `o.Ssr.CircuitBreaker.Enabled = false` |
| Page size | Pages whose JSON is over `MaxPageBytes = 2 MiB` are not sent to the server | `o.Ssr.MaxPageBytes = long.MaxValue` |

A component that throws during SSR (a structured `500` from the server) does not count toward the circuit breaker.

`InertiaSsrException.Type` has new values: `overloaded`, `circuit-open`, `not-ready`, `page-too-large` and `bundle-missing`, besides
`connection`, `timeout`, `http`, `response`, `response-too-large`, `unknown`, `startup` and the server's own error types.

## The SSR HTTP client

The `Inertia.Net.Ssr` client (now also `InertiaSsr.HttpClientName`) no longer follows redirects and no longer uses the system proxy. A
renderer that answers with a redirect is now an `http` failure: point `Ssr.Urls` at the final address. If your renderer is only
reachable through a proxy, configure the client's primary handler yourself:

```csharp
builder.Services.AddHttpClient(InertiaSsr.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, Proxy = new WebProxy("http://proxy:3128") });
```

A plain `http://` SSR URL that is not loopback now logs a warning at startup.

## Managed Node process (`UseNodeProcess()`)

- **Port.** `Ssr.Url` no longer sets the managed port: use `n.BasePort` (default `13714`). Setting `Ssr.Urls` together with
  `UseNodeProcess()` fails validation. The bundle must still listen on that port (see `inertia({ ssr: { port } })`).
- **Network exposure.** An instance that also answers on a non-loopback address is now stopped and not restarted (1.2 logged a warning).
  Build the SSR bundle with `inertia({ ssr: { host: '127.0.0.1' } })`, or set `n.AllowNetworkExposure = true` to keep 1.2's warning.
- **Liveness.** Each running instance is checked with `GET /health` every `n.LivenessInterval` (5 s); after `n.LivenessFailureThreshold`
  (3) failures in a row it is killed and restarted. Raise both for a renderer whose event loop can be busy for several seconds.
- **Several instances.** `n.Instances = 4` runs four processes on `BasePort` to `BasePort + 3`. Each gets its port in `INERTIA_SSR_PORT`,
  so the SSR entry must listen on it:

  ```tsx
  createServer((page) => createInertiaApp({ /* ... */ }), { host: '127.0.0.1', port: Number(process.env.INERTIA_SSR_PORT ?? 13714) })
  ```

  A bundle built by `@inertiajs/vite` from a plain `createInertiaApp()` entry listens on its baked-in port and only works with one instance.
- Startup in `Required` mode fails when no instance stays up (for example because it was stopped for network exposure).

## Health check

`AddInertiaSsr()` no longer calls `GET /health`. It reports the gateway's state: healthy while SSR is disabled, goes through the Vite dev
server, or every server can take renders; degraded while some circuits are open or some managed instances are not ready; the
registration's failure status when none can. A server that went down is reported healthy until renders fail. `InertiaSsrHealthCheck` can
no longer be constructed directly; register it with `AddInertiaSsr()`.

## Options are read once

SSR options are read at startup. Changes made later through `IOptionsMonitor<InertiaOptions>` (for example a reloaded configuration
file) no longer reach the SSR gateway; restart the app instead.

## Lazy props with dot keys below them

A lazy prop that has dot keys below it, such as `["auth"] = Prop(() => LoadAuth())` next to `["auth.user"] = ...`, now stays lazy: its
loader runs only when `auth` is part of the response, then the dot keys are applied to a copy of its value. In 1.2 the loader ran while
the props were unpacked, even on partial reloads that did not ask for `auth`. If such a loader had side effects that other props relied
on, move them out of the loader.

## New, opt-in

- `InertiaProp.Parallel()` starts independent async loaders together, at most `o.Props.MaxConcurrency` (default 4) at a time per render.
  Loaders stay sequential unless marked, so a shared `DbContext` remains safe; a parallel loader needs its own (`IDbContextFactory<T>`).
- Metrics and traces: `.WithMetrics(m => m.AddMeter(InertiaDiagnostics.MeterName))` and
  `.WithTracing(t => t.AddSource(InertiaDiagnostics.ActivitySourceName))`. The README lists the instruments.

See the [README](../README.md#server-side-rendering) for the full SSR guide and [protocol.md §8](protocol.md#8-ssr) for the exact rules.
