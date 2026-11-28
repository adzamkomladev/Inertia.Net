# Inertia.Net.Live.Sse

[Live props](https://github.com/adzamkomladev/Inertia.Net#live-props) need the Inertia client 3.11.0 or later.

The Server-Sent Events transport for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net) live props. The app serves one
`text/event-stream` endpoint, and the browser reads it with its built-in `EventSource`. There is no client library to install and no
hub. Native AOT compatible.

**One instance only.** A publish reaches the clients connected to the instance that publishes it, and there is no backplane. For
several instances behind a load balancer, use [Inertia.Net.Live.SignalR](https://www.nuget.org/packages/Inertia.Net.Live.SignalR)
with a backplane.

```sh
dotnet add package Inertia.Net.Live.Sse
```

## Server

```csharp
builder.Services.AddInertia();
builder.Services.AddInertiaLive().AddSse(o =>
    // Private, presence and encrypted-private channels are refused unless the authorizer allows them.
    o.Authorize((context, channel, ct) => ValueTask.FromResult(channel.Name == $"users.{context.UserIdentifier}")));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseInertia();
app.MapInertiaLiveSse();   // /inertia-live; chain .RequireAuthorization() to require a signed-in user
```

Publish with `IInertiaLive` as usual. Every client connected to the channel gets the event:

```csharp
await live.PublishAsync(OrderEvents.Updated(id), exceptSocketId: context.InertiaRequest().SocketId);
```

## Client

New apps from `dotnet new inertia` include the transport as `ClientApp/lib/inertia-sse.ts`. Otherwise copy
[the file](https://github.com/adzamkomladev/Inertia.Net/blob/main/src/Inertia.Net.Templates/content/common/ClientApp/lib/inertia-sse.ts)
into your app. Then enable it:

```ts
import { createInertiaApp } from '@inertiajs/react'   // or @inertiajs/vue3, @inertiajs/svelte
import { sse } from './lib/inertia-sse'

createInertiaApp({ live: sse() })
```

| Option | Default | Effect |
|---|---|---|
| `url` | `'/inertia-live'` | The path given to `MapInertiaLiveSse`. |
| `throttle` | `1000` | Inertia's shortest time between two reloads of a prop, in milliseconds (a prop's `.Throttle(...)` wins). |
| `pauseWhenHidden` | Inertia's default | Holds reloads while the tab is hidden. |
| `onConnection` | none | Called once the first stream opens, with `{ state, stop() }` for tests and diagnostics. |

The stream opens on the first subscription and closes when the page leaves its last channel. The channels are part of the stream URL,
so the stream reopens when a page joins or leaves a channel. Joins and leaves in one visit are batched into one reopen, and a reopen
does not count as a reconnect. When the stream drops, `EventSource` reconnects on its own. A refused stream (403 or 400) is tried
again with a backoff of up to 30 seconds. After a reconnect, Inertia reloads every live prop once, so events missed while offline
are caught up.

## Channel authorization

The rules match the SignalR transport, and the same `ILiveChannelAuthorizer` serves both:

| Channel | Who may join |
|---|---|
| `Public` | Anyone who can reach the endpoint. Set `o.AuthorizePublicChannels = true` to ask the authorizer about these too. |
| `Private`, `Presence`, `EncryptedPrivate` | Only connections the authorizer allows. Without an authorizer, nobody. |

Use `o.Authorize((context, channel, ct) => ...)` for a one-line rule, or `o.Authorize<OrderChannelAuthorizer>()` for a class with
constructor injection. The `HubCallerContext` the authorizer gets wraps the stream request. It carries `User`, `UserIdentifier` (the
name identifier claim), `ConnectionId` (the socket id), `ConnectionAborted` and `GetHttpContext()`. A refused channel is listed in
the `connected` event, and the client logs a warning for it. The other channels still work.

## Wire format

`GET /inertia-live?channel=private:orders.1&channel=public:feed` opens the stream. Each `channel` value is `"{type}:{name}"`.

| Event | Data |
|---|---|
| `connected` | `{ "socketId": "…", "refused": ["private:orders.2"] }`, sent first. The socket id is the client's `X-Socket-Id`. |
| `inertia` | `{ "channel": "private:orders.1", "payload": … }`, where the payload is the [broadcast envelope](https://github.com/adzamkomladev/Inertia.Net/blob/main/docs/protocol.md#broadcast-payload). |
| `ping` | Empty. Sent every `HeartbeatInterval` while the stream is idle, so proxies keep it open. |

## Options

| Option | Default | Effect |
|---|---|---|
| `AuthorizePublicChannels` | `false` | Asks the authorizer about public channels too. |
| `MaxSubscriptionsPerConnection` | `64` | More channels on one stream get 400. |
| `MaxChannelNameLength` | `200` | A longer channel name is refused. |
| `MaxQueuedEvents` | `256` | Events waiting for a slow client. When the queue is full, the stream closes, and the client reconnects and reloads its live props. |
| `HeartbeatInterval` | 15 seconds | How often an idle stream gets `ping`. |
| `AllowedOrigins` | empty | Origins allowed to connect besides the app's own. |

## Security

- **Origin check.** A request whose `Origin` is neither the app's own nor in `o.AllowedOrigins` gets 403. Configure trusted forwarded
  headers behind a proxy so the check sees the external scheme and host.
- **Authentication expiry.** A stream ends when its authentication ticket expires. The client reconnects, and each channel is
  authorized again. Authorization runs when the stream opens, so a change in permissions takes effect on the next stream.
- **Same values for everyone.** Every subscriber of a channel receives the same published values. When a value depends on the user,
  publish without values and let each client reload its own.
- **`X-Socket-Id` is client-supplied.** It is only ever used to leave a connection out, never to identify a user.

## Native AOT

The package is annotated `IsAotCompatible`, and its JSON is written without reflection.

Documentation: <https://github.com/adzamkomladev/Inertia.Net#server-sent-events>
