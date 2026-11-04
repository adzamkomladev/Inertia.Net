# Review fixes: upstream checks (2026-11-04)

## Precognition and rate limiting (plan 2.1)

Checked `laravel/precognition` main at `3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b`.

- The validator declares `let debounceTimeoutDuration = 1500`; it configures lodash debounce with
  `{ leading: true, trailing: true }`. A burst can therefore send its first validation immediately and another after the quiet
  interval. `setValidationTimeout` changes the debounce interval, not the HTTP request deadline. The validator separately defaults
  that deadline to five seconds. [Validator source](https://github.com/laravel/precognition/blob/3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b/packages/core/src/validator.ts#L210-L302)
- For both successful and unsuccessful HTTP responses, a precognitive request first checks for the response header
  `Precognition: true`. An absent header produces the client's missing-Precognition-response error before status-specific
  handling. The server's rate-limit rejection hook should set that header for precognitive requests.
  **A 429 still rejects:** there is no dedicated 429 handler in the client, and the validator suppresses only cancellation and
  expected 422 errors. The header prevents a misleading protocol error; it does not turn a throttled request into successful
  validation. Applications should handle the rejected request or show a retry message.
  [Client source](https://github.com/laravel/precognition/blob/3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b/packages/core/src/client.ts#L79-L211),
  [validator error handling](https://github.com/laravel/precognition/blob/3a186e29e6e3e4dbda65f9b1160afc4b5430cb0b/packages/core/src/validator.ts#L246-L273)
- Laravel documents that precognitive requests execute route middleware while skipping the controller, recommends checking
  precognition before middleware side effects, and shows `setValidationTimeout(3000)`. Its React example validates on blur.
  [Precognition guide](https://laravel.com/docs/13.x/precognition#managing-side-effects),
  [React example](https://laravel.com/docs/13.x/precognition#using-react)
- Laravel's routing guide describes named rate limiters, request-derived partition keys and attaching throttling middleware to
  routes. The Inertia.Net recipe should retain a bounded validation bucket and a separate strict submission bucket, selecting
  the validation bucket only when endpoint metadata guarantees the handler is skipped. This partition design is an adapter
  recommendation based on that middleware ordering, rather than a Laravel-prescribed Precognition policy.
  [Laravel rate limiting](https://laravel.com/docs/13.x/routing#rate-limiting)

Plan wording correction: replace “adds `Precognition: true` to the 429 so the client does not throw” with “adds
`Precognition: true` to the 429 so the client recognizes the response; the throttled request still rejects and needs a retry/error
UI.” No unbounded exemption should be introduced.

## Kestrel response completion and HTTP/1.1 (plan 1.2)

Checked ASP.NET Core `v10.0.14`, commit `0ef4bbfa3291b306a21e5001cb0491277bdd35bc`.
`Http1Connection` inherits `HttpProtocol`; that protocol's request-processing method contains the keep-alive loop. Within each
iteration, it finishes the response and then executes:

```csharp
await FireOnCompleted();
```

Only after completion callbacks finish does it dispose the context, drain remaining request-body state and begin the next loop
iteration. `FireOnCompleted` itself awaits each callback. Consequently an awaited refresh in `OnCompleted` can delay the next
request on the same HTTP/1.1 connection even though the prior response bytes have reached the client. HTTP/2 and HTTP/3 use this
logic per stream and should not be described as serializing every request on their shared connection.
[HTTP/1 implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Servers/Kestrel/Core/src/Internal/Http/Http1Connection.cs#L14),
[request loop and callback await](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L628-L785),
[callback processing](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L838-L865)

The plan's premise is confirmed. The callback must only enqueue owned background work and return promptly; the worker owns its
scope, shutdown token and refresh-key cleanup. A same-connection regression test must force HTTP/1.1 and one connection, rather
than rely on the client's default protocol or connection pooling.
