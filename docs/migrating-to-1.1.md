# Migrating to Inertia.Net 1.1

Update all Inertia.Net packages to 1.1.0 together. The release retains public method signatures and the Inertia v3 JSON protocol. There are no API removals, database migrations or cookie/session/TempData storage format changes. Test the following observable changes before deploying.

| Area | Application action |
|---|---|
| Indexed array reloads | Selecting `rows.1.extra` returns the complete `rows` array, including siblings and all nested loaders. A descendant `except` excludes the entire array; `Always` still wins. Use objects keyed by ID for independent updates, or defer the parent array. Expect more loader work and larger responses in affected requests. |
| Serialization | Review snapshots for registered polymorphic discriminators and derived fields, default omission, type/member numeric string formatting, and serialization callbacks. Declared-type contracts remain authoritative; unregistered derived types follow System.Text.Json's configured fallback or failure behavior. |
| Collection validation | Error keys now use `items.0.email` instead of `items[0].email`. Update custom consumers and tests. Dictionary keys are preserved. FastEndpoints uses its binding serializer's naming policy, which may differ from HTTP JSON options. The existing `[JsonPropertyName]` validation limitation remains. |
| DOM big integers | With `PreserveBigIntegers = true`, unsafe integer values in `JsonElement` and `JsonNode` become `{"$bigint":"..."}`, including flash and restored redirect state. The disabled setting retains ordinary DOM output. |
| SSR response limit | Success and error responses are limited to 16 MiB of actual response bytes. Increase `Ssr.MaxResponseBytes` or explicitly set it to `null` for intentionally larger responses. Nonpositive limits are invalid. Oversized responses fall back to client rendering, or throw `InertiaSsrException` with `Type = "response-too-large"` when `ThrowOnError` is enabled. |
| Managed SSR | Use a dedicated loopback endpoint and bind Node explicitly to `127.0.0.1`. Occupied endpoints prevent managed startup. Render requests require a ready, live child and otherwise follow the configured fallback or throw behavior; shutdown kills that process tree without calling the URL's `/shutdown` route. For externally managed or remote servers, enable SSR without calling `UseNodeProcess()`. |

Restart the host when changing a managed SSR URL. Readiness belongs to the endpoint used at process startup; changing the URL cannot redirect page props to another service.

`Ssr.Timeout` is now the sole rendering deadline, including responses that previously hit HttpClient's 100-second default. `Timeout.InfiniteTimeSpan` disables the deadline; other negative or unsupported durations are invalid. Request cancellation propagates and stops further prop loaders. Loaders remain sequential, so sharing an EF `DbContext` retains its existing execution model.

Nonempty buffered responses retain their status. Flash and history changes first created by a share factory or prop loader reach the rendered response. Omitted or rescued direct array elements keep their positions as `null`.

```csharp
builder.Services.AddInertia(o =>
{
    o.Ssr.Url = "http://127.0.0.1:13714";
    o.Ssr.MaxResponseBytes = 32L * 1024 * 1024; // optional override; null disables the limit
    o.Ssr.UseNodeProcess();
});
```

```ts
inertia({ ssr: { entry: 'resources/js/ssr.tsx', host: '127.0.0.1', port: 13714 } })
```

For a hand-written entry, pass the host to `createServer(render, { host: '127.0.0.1', port: 13714 })`. Remote deployments must configure the listener host explicitly and use unmanaged mode in .NET.

See [protocol deviations](protocol.md#deliberate-deviations-from-laravel) and [performance measurements](../tests/Inertia.Net.Benchmarks/RESULTS.md) for workload details and limitations.
