# Migrating to Inertia.Net 1.2

Update all Inertia.Net packages together. The release keeps every public method signature, the Inertia v3 JSON protocol and the
cookie, session and TempData storage formats. It adds API (`UseInertiaAntiforgery()`, `InertiaAntiforgeryOptions`,
`InertiaStateOptions.CookieSecurePolicy`) and changes the observable behavior below. Test these before deploying.

## Recommended: turn on CSRF protection

Inertia.Net 1.1 only issued the `XSRF-TOKEN` cookie (`UseInertiaAntiforgeryCookie()`); validating it was left to the app, and
`UseAntiforgery()` does not check JSON bodies, which is what the Inertia client sends. Replace the cookie-only setup:

```csharp
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);

app.UseAuthentication();       // when the app signs users in
app.UseInertia();
app.UseInertiaAntiforgery();   // replaces UseInertiaAntiforgeryCookie() and UseAntiforgery()
```

Every POST, PUT, PATCH and DELETE then needs the token, including requests from outside the browser. Exempt them explicitly:

```csharp
app.MapPost("/webhooks/stripe", HandleStripe).DisableAntiforgery();
app.UseInertiaAntiforgery(o => o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/api"));
```

Rejected requests get `419`. To show a message instead of the client's error dialog, return
`Back().WithFlash(...)` from `o.OnRejected`. `UseInertiaAntiforgeryCookie()` is unchanged apart from the essential flag below.

## Behavior changes

| Area | Change | Application action |
|---|---|---|
| `XSRF-TOKEN` cookie | Marked essential, so a cookie-consent policy (`CheckConsentNeeded`) no longer suppresses it. | None, unless your consent policy must list it: it is a strictly necessary security cookie. |
| `Back()` | On an HTTPS request, an `http` Referer with the request's host and port is upgraded to `https` instead of followed as-is. | None. Plain-HTTP requests (including behind a proxy that does not forward the scheme) behave as before. |
| Root views | `RootView` and `WithRootView(...)` paths that resolve outside the content root (absolute paths, `..`) throw `InvalidOperationException`. | Move templates under the content root, or use a relative path. Razor root view names (`UseRazorRootView`) are unaffected. |
| Relaxed app encoder | With a custom `JsonSerializerOptions.Encoder` on the HTTP JSON options (for example `UnsafeRelaxedJsonEscaping`), Inertia's page serializer escapes `<`, `>`, `&`, `'`, `"`, `+`, `` ` `` and `/` in declared property names, in JSON and HTML responses alike. Values in JSON responses still use your encoder. Non-ASCII text stays unescaped. | Only byte-exact snapshot tests of property names with those characters change; the parsed JSON is identical. |
| Missing files in production | A Vite manifest, root template or `Ssr.BundlePath` that does not exist is re-checked at most once per second, instead of being cached as missing until restart. A file that exists is still never re-read in production. | None. Deployments that copy assets after the app starts now recover without a restart. |
| SSR bundle | `Ssr.BundlePath` presence is cached: in production, deleting the bundle while the app runs no longer turns SSR off. | Restart the app to turn SSR off, or set `Ssr.Enabled = false`. |
| Session and TempData stores | `UseSession()` and `UseTempDataStateStore()` do not load the session or TempData when the request has no session or TempData cookie. | None. A custom `ITempDataProvider` is always loaded as before. |
| Typed dictionaries | A `Dictionary<TKey, TValue>` whose values contain `InertiaProp` members is walked: those props resolve, defer and filter like object members, instead of the render throwing. | None. Dictionaries without props still serialize in one call. |
| Managed SSR | A warning is logged when the Node SSR server also answers on a non-loopback address. | Build the SSR bundle with `inertia({ ssr: { host: '127.0.0.1' } })`. |
| `@vite(...)` errors | A failing `@vite` token (missing manifest or entry) throws before any HTML is written, instead of after part of the page. | None. |

## Internal changes worth knowing

- SSR `head` and `body` are kept as UTF-8 bytes. A response with invalid UTF-8 or an unpaired `\uD800` escape is now a `response`
  failure (falls back to client rendering, or throws with `ThrowOnError`) instead of being decoded with replacement characters.
- The MVC tag helpers write `IHtmlContent` that streams UTF-8. Razor views that call `InertiaRootViewContext.GetBodyHtml()` directly
  keep working unchanged.

## Repository and release process

The release workflow now runs in a `release` GitHub environment. Add required reviewers to it and move `NUGET_API_KEY` to an environment
secret (Settings > Environments > release), so that pushing a tag alone cannot publish. The build fails on known-vulnerable NuGet
packages, direct or transitive, at any severity (`NuGetAuditMode=all`, `NuGetAuditLevel=low`).

See the [changelog](../CHANGELOG.md), the [review](reviews/2026-10-18-security-performance-review.md) behind these changes, and the
[measurements](../tests/Inertia.Net.Benchmarks/RESULTS.md).
