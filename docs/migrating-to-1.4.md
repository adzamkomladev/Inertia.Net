# Migrating to 1.4

1.4 adds features and changes one default. Public signatures are unchanged apart from additions, and the Inertia v3 JSON protocol and
the cookie, session and TempData formats are the same as in 1.3. Update all Inertia.Net packages together.

## The one behavior change: redirects to another origin

An Inertia request is an XHR. When it gets a redirect to another origin, the browser follows it with XHR and blocks the answer unless
that origin allows CORS, so the visit fails. In 1.4 `UseInertia()` turns such a redirect into a `409` with `X-Inertia-Location`, and the
client makes a full page visit, as `Location(url)` always did.

| Response to an Inertia request | 1.3 | 1.4 |
|---|---|---|
| `Results.Redirect("/dashboard")` | 302 | 302 (unchanged) |
| `Results.Redirect("https://same-host.example/x")` on that host | 302 | 302 (unchanged) |
| `Results.Redirect("https://accounts.example.com/login")` | 302, which the browser blocks | `409` + `X-Inertia-Location` |
| `Results.Redirect("//cdn.example.com/x")` | 302 | `409` + `X-Inertia-Location` |
| `http://` redirect on an `https` request, same host | 302 | `409` + `X-Inertia-Location` (another origin) |
| any redirect on a prefetch (`Purpose: prefetch`) | 302 | 302 (unchanged) |
| `Location(url)` | 409 | 409 (unchanged) |

Flash data, errors and history flags travel with the 409 as they do with a redirect.

**Check.** Search for redirects built from absolute URLs (`Results.Redirect`, `Redirect(...)` in MVC, `Send.RedirectAsync` in
FastEndpoints). Behind a TLS-terminating proxy that does not forward the scheme, an absolute `https://` redirect to your own host looks
like another origin (the request is `http`); configure forwarded headers (see "Deploying behind a proxy" in the README) or use relative
redirects. To keep the 1.3 behavior: `AddInertia(o => o.ConvertExternalRedirects = false)`.

## Small things you may notice

- `AddInertia()` registers the memory cache (`AddMemoryCache()`), used by `InertiaProp.Cache()`. An app that registers its own
  `IMemoryCache` first keeps it.
- `AddInertia()` registers a hosted service that writes the TypeScript declarations. It does nothing unless
  `o.TypeScript.OutputPath` is set and the app runs in Development.
- A render allocates 8 bytes more (JSON responses) and 24 bytes more (HTML) for the new per-response settings.

## New features to consider

| Feature | Turn it on |
|---|---|
| Cached props | `Prop(...).Cache("key", TimeSpan.FromMinutes(5))` |
| SSR response cache | `o.Ssr.Cache.Duration = TimeSpan.FromMinutes(5)` or `Render(...).WithSsrCache(...)` |
| Chunk prefetching | `o.Vite.Prefetch = VitePrefetch.Waterfall` |
| CSP nonces | `o.CspNonce = ctx => ctx.Items["csp-nonce"] as string` |
| Cacheable pages with CSRF | `app.UseInertiaAntiforgery(o => o.CookieRefresh = XsrfCookieRefresh.Lazy)` |
| TypeScript declarations | `o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts"` plus `o.TypeScript.Page<T>(component)` |
| Inertia DevTools | `services.AddInertiaDevTools()` and `app.MapInertiaDevTools()` |
| Missing page check | `o.Pages.Directory = "ClientApp/pages"` |
| Page endpoints | `app.MapInertia("/about", "About")`, `this.InertiaView(props)` in MVC |
| A new app | `dotnet new install Inertia.Net.Templates`, then `dotnet new inertia` |
