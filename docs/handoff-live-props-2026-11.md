# Handoff: Inertia v3 live props for Inertia.Net (reassessment request)

Date: 2026-11-03. Status: **investigation only. No code has been written and no repository file has been changed** apart from this
document. Read [AGENTS.md](../AGENTS.md) and [docs/protocol.md](protocol.md) first; they govern any later work.

## Your task

Independently reassess whether and how Inertia.Net should support Inertia's new **live props** feature, and review the design
below. Treat everything under "Claims to verify" as unproven. Several of them come from AI summaries of web pages, not from reading
source. Confirm or refute each against primary sources before relying on it. Do not implement anything unless asked; the output
wanted is a verdict, corrections to this document, and a recommended scope.

Please answer:

1. Is the wire contract in section 2 correct and complete? Anything missing (other page fields, request headers, client events)?
2. Are the collection rules in section 3 right, and are they the right rules for Inertia.Net given the deviations already in
   `docs/protocol.md`?
3. Is the proposed `.Live()` API (section 4) the right shape for .NET, or is there a better one?
4. Is the SignalR design (section 6) sound? Which gotchas are missing? Is a separate package the right call?
5. What is the right phase split and minimum viable scope? Is anything here not worth building?

## 1. What the feature is

Docs: <https://inertiajs.com/docs/v3/data-props/live-props>. A live prop is a page prop that the client refreshes when a broadcast
event arrives. The client subscribes to channels and events, and runs an ordinary partial reload for the affected props. A
broadcast event can also carry new prop values so no request is needed. Transports are all client side: `echo()`, `pusher({client})`,
`actionCable({consumer})`, or a custom `LiveTransport` / `LiveChannelTransport`. The server never speaks WebSocket; it only
declares what to listen to and (optionally) broadcasts.

Laravel API (for reference): `Inertia::live($value, on:, channel:, throttle:)`, `Inertia::defer(...)->live(on:)`,
`Inertia::broadcastProps([...])` inside an event's `broadcastWith()`. Merge props cannot be live. `on` takes event instances or class
names, from which Laravel derives channels and the broadcast name. That derivation has no .NET equivalent.

**Client availability.** Live props first ship in `@inertiajs/core` **3.11.0** (published 2026-11-02). I downloaded 3.9.0, 3.10.0 and
3.11.0 with `npm pack` and grepped `dist/`: `liveProps` appears only in 3.11.0. Repo samples pin `^3.9.1`.

## 2. Wire contract

### Verified by reading the shipped 3.11.0 client (`@inertiajs/core/dist/index.js`)

Reproduce with:

```sh
mkdir -p /tmp/core && cd /tmp/core && npm pack @inertiajs/core@3.11.0 && tar xzf *.tgz
grep -n "liveProps\|__inertia\|X-Socket-Id\|socketId" package/dist/index.js
```

- **Page field `liveProps`**: object keyed by prop path. Each value has `listeners` (array) and optional `throttle` (ms).
  Each listener is `{ channel: { name, type, params? }, events: string[] }`. `type` is `public | private | presence |
  encrypted-private` (`LiveChannelType` in `types.ts`).
- **Partial-response merge** (around `index.js:3221`): the client does `{...page.get().liveProps, ...response.liveProps}`, so a
  partial response only needs entries for props it includes, exactly like `scrollProps`.
- **Subscriptions** (`Live.sync`, ~`index.js:5365`): one subscription per `(channel, event)`, built from every prop's listeners.
  A transport with `subscribeToChannel` gets one subscription per channel and routes events itself via `eventName(payload)`,
  defaulting to `payload.__inertia.event`.
- **Request header `X-Socket-Id`** (~`index.js:5254`): added to same-origin requests when the transport provides a `socketId()`
  and the header is not already set.
- **Event envelope**: the payload may carry `__inertia: { event?, props?, socketId? }`. `props` is a map of prop path to value.
  An event whose `socketId` equals the client's own is dropped. Props that are not present in `props`, or are already refreshing,
  fall back to a normal partial reload.
- **Reconnect**: when `onStatusChange(connected)` goes false then true, every requested live prop is force-refreshed.
- **Defaults**: throttle 1000 ms per prop, 32 ms collect window, `pauseWhenHidden` true.
- **Pusher transport** (`dist/pusher.js`) reads as a model for a custom transport: channel name prefixes are `""`, `private-`,
  `presence-`, `private-encrypted-`.

### Claims to verify (from summaries; not read as source)

- The docs page says nothing about the page JSON, so the shape above is from the client, not the docs.
- Key position of `liveProps` in the page object. Unknown. `docs/protocol.md` §1 documents inertia-laravel's key order, so find
  where `liveProps` goes (`src/Response.php` / `PropsResolver::buildMetadata()`).
- Exact content of `broadcastProps` on the Laravel side (how the `__inertia` envelope is built, whether `socketId` is set
  server-side, how dotted keys resolve). Only the client half is verified.

## 3. Server collection rules (from a summary of `inertia-laravel` 3.x `PropsResolver.php`; verify against source)

Files in `inertiajs/inertia-laravel@3.x/src`: `LiveProp.php`, `HasLiveUpdates.php`, `SupportsLiveUpdates.php`, `PropsResolver.php`.

- Metadata key `liveProps`, emitted only when non-empty. Entry per path: `listeners` and, only when non-null, `throttle`.
- Collected in `excludeFromInitialResponse()` **before** the defer, optional and once exclusions, so an excluded prop still
  reports its listeners ("excluding a prop never removes its live listeners").
- Also collected for included props in `collectMetadata()`.
- A rescued prop skips `collectMetadata()` and so gets no entry.
- On partial requests a prop that fails the only/except filter is skipped. Always props and children of an already resolved parent
  bypass the filter.
- `HasLiveUpdates::live()` throws on merge props. Events are grouped by channel, and an explicit channel applies only to the events
  it was passed with.
- A later collection for the same path overwrites the earlier one.

Question for you: do these rules need Inertia.Net-specific adjustments? Check how they meet our existing rules: whole-array
reloads (deviation 11), dot-key lazy parents (deviation 7), `Cache()`, `Parallel()`, and "any prop can be Once/Rescue/merge/defer"
(deviation 8).

## 4. Proposed core API (not implemented)

```csharp
["order"] = Prop(() => db.Orders.Find(id))
    .Live(LiveChannel.Private($"orders.{id}"), "OrderUpdated", throttle: 500),

["stats"] = Defer(() => stats.Load()).Live(LiveChannel.Public("stats"), "StatsChanged"),
```

- `Live()` is repeatable (adds listeners). Calling it on a merge or scroll prop throws `InvalidOperationException`.
- Explicit channel and event names; no event-class derivation.
- Storage: one `LiveSpec?` reference on `InertiaProp`, in line with the 8-byte note at `src/Inertia.Net/InertiaProp.cs:34`.

Code touch points (verified to exist at these lines on `main`, `5d0fa8a`):

- `src/Inertia.Net/InertiaProp.cs`: prop flags and fluent methods.
- `src/Inertia.Net/PropsWriter.cs`: `WriteMetadata` (line 252) writes the metadata in protocol order; `CollectMetadata` (756) and
  `CollectOnce` (832) are the models to follow, including the `_isPartial && !IsIncludedInPartialMetadata()` guard; the initial-load
  exclusion path is at lines 716-730.
- `src/Inertia.Net.Testing/InertiaPage.cs:56`: add a `LiveProps` accessor next to `OnceProps`.
- `src/Inertia.Net/InertiaDevTools.cs:584` reads `onceProps` from the page JSON; check whether DevTools should show live props.
- `src/Inertia.Net/InertiaTypeScript.cs`: check whether it enumerates metadata fields.
- Docs: `docs/protocol.md` §1 table and §4, READMEs, `CHANGELOG.md`.
- Tests: `tests/Inertia.Net.Tests/{PropsResolverTests,PropsWriterTests,PageWriterTests}.cs`, `tests/Inertia.Net.Testing.Tests/ParsingTests.cs`
  and the protocol conformance suites (Minimal API, MVC, FastEndpoints) in `tests/Inertia.Net.IntegrationTests`.
- Native AOT: metadata is written with `Utf8JsonWriter`, so no reflection is needed. `tests/Inertia.Net.AotSmoke` and
  `bash eng/aot-smoke.sh` exist.

Also: add `InertiaRequest.SocketId` (reads `X-Socket-Id`); see `src/Inertia.Net/InertiaRequest.cs` for the header constants.

## 5. Phase proposal

1. Core: `liveProps` metadata, `.Live()`, `LiveChannel`, testing accessor, protocol doc, tests. Useful with any transport.
2. `InertiaRequest.SocketId`, and optionally an envelope helper for `broadcastProps`-style value delivery (needs the Laravel
   source read first).
3. `Inertia.Net.SignalR` package, plus a JS transport and a sample.

## 6. SignalR design (proposal; nothing built)

Why SignalR: no extra infrastructure for .NET apps. Alternative: any Pusher-protocol server (Reverb, Soketi) works with the stock
`pusher()` transport and no custom client code, but needs a Pusher HTTP publish and an auth endpoint on the .NET side.

Mapping:

| Inertia | SignalR |
|---|---|
| channel | a group named `"{type}:{name}"` |
| channel authorization | hub `Subscribe(type, name)` checks an authorizer, then `Groups.AddToGroupAsync` |
| event name | `__inertia.event` in the payload, not a SignalR method name |
| `X-Socket-Id` | `connection.connectionId` |
| `toOthers` | `Clients.GroupExcept(group, socketId)` |
| reconnect refresh | `onreconnected` then `onStatusChange(true)` |

Use a **channel-style transport** (`subscribeToChannel` + `eventName`): SignalR `connection.on(method)` handlers are per
connection, not per channel, so an event-style transport would cross channels that share an event name. The server sends one
method, `"inertia"`, with `(channelKey, payload)`; the transport dispatches by channel key.

Server sketch: `AddInertiaLive()`, `MapInertiaLiveHub("/inertia-live")`, a channel authorizer delegate, and
`IInertiaLive.PublishAsync(channel, event, props?, except?)` over `IHubContext`. Client sketch: ~40 lines of TypeScript over
`@microsoft/signalr` (`HubConnectionBuilder`, lazy `start()`, `invoke("Subscribe")`).

Gotchas I identified (please look for more):

1. A reconnect loses group membership and changes the connection id. The transport must re-`Subscribe` every active channel
   **before** reporting `connected = true`, or the client refreshes once and goes silent.
2. `subscribe` must not throw (the docs say an error aborts the page swap). Wrap start/invoke in `.catch`.
3. Scale-out needs a backplane (Redis or Azure SignalR); groups are per server.
4. `X-Socket-Id` is client supplied. A client can only exclude a connection id it already knows, from broadcasts that client itself
   triggered. Low risk; treat as untrusted.
5. Presence channels: Inertia only subscribes; treat as `private` for authorization, no member tracking.
6. JSON casing: SignalR's JSON protocol camelCases POCO properties, not dictionary keys. Pin `__inertia` with `[JsonPropertyName]`
   or use a dictionary.
7. Every subscriber to a channel receives the same broadcast values; authorization runs only on subscribe. Docs must say so.

**Unverified: whether ASP.NET Core SignalR hubs work under Native AOT on .NET 10.** The repo requires AOT for core, so a separate
package keeps any limitation out of it. Check the current ASP.NET Core docs before committing to that.

Open packaging question: the repo publishes no npm package. Options are (a) ship the JS transport in
`src/Inertia.Net.Templates/content/*` and the docs as copy-paste, (b) publish `@inertia-net/signalr`. I lean toward (a) first.

## 7. Checks run and not run

- Run: `npm pack` of `@inertiajs/core` 3.9.0, 3.10.0, 3.11.0 and grep of the 3.11.0 `dist`; reading `docs/protocol.md`,
  `InertiaProp.cs`, parts of `PropsWriter.cs`, `PropsPlanner.cs`.
- Not run: no build, no tests, no prototype, no SignalR experiment, no Native AOT check.
- Not read as source: all of inertia-laravel (only summarized by a fetch tool), the client `live.ts` as TypeScript source (read the
  compiled `dist` instead), the Laravel `broadcastProps` implementation.

## 8. Suggested method for the reassessment

1. Re-derive section 2 and 3 from primary sources (the two upstream repos, `3.x` branches) and diff against this document.
2. Prototype the phase 1 metadata path in a branch against `PropsWriter` and write the failing conformance tests first
   (see the rule in `docs/plan-review-fixes-2026-11.md`).
3. Spike the SignalR transport against the client from `@inertiajs/core@3.11.0` in a sample, with a Playwright test that covers a
   reconnect, since gotcha 1 is the likeliest silent failure.
4. Report which checks you ran, and any you could not.
