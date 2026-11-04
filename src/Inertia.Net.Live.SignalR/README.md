# Inertia.Net.Live.SignalR

[Live props](https://github.com/adzamkomladev/Inertia.Net#live-props) need the Inertia client 3.11.0 or later.

The SignalR transport for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net) live props: a hub the browser subscribes to,
channel authorization, and the `IInertiaLiveBroadcaster` that `IInertiaLive` publishes through. Nothing else to host: the hub runs in
your app. Native AOT compatible.

```sh
dotnet add package Inertia.Net.Live.SignalR
```

## Server

```csharp
builder.Services.AddInertia();
builder.Services.AddInertiaLive().AddSignalR(o =>
    // Private, presence and encrypted-private channels are refused unless the authorizer allows them.
    o.Authorize((context, channel, ct) => ValueTask.FromResult(channel.Name == $"users.{context.UserIdentifier}")));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseInertia();
app.MapInertiaLiveHub();   // /inertia-live; chain .RequireAuthorization() to require a signed-in user
```

Publish with `IInertiaLive` as usual; every subscriber of the channel gets the event:

```csharp
await live.PublishAsync(OrderEvents.Updated(id), exceptSocketId: context.InertiaRequest().SocketId);
```

## Client

Add `@microsoft/signalr` with your package manager: `npm install @microsoft/signalr`, `bun add @microsoft/signalr`, `pnpm add @microsoft/signalr` or `yarn add @microsoft/signalr`.

New apps from `dotnet new inertia` ship the transport as `ClientApp/lib/inertia-signalr.ts`; otherwise copy
[the file](https://github.com/adzamkomladev/Inertia.Net/blob/main/src/Inertia.Net.Templates/content/common/ClientApp/lib/inertia-signalr.ts)
into your app. Then enable it:

```ts
import { createInertiaApp } from '@inertiajs/react'   // or @inertiajs/vue3, @inertiajs/svelte
import { signalr } from './lib/inertia-signalr'

createInertiaApp({ live: signalr() })
```

`@microsoft/signalr` loads on the first subscription, so pages without live props and server-side rendering never load it.

| Option | Default | Effect |
|---|---|---|
| `url` | `'/inertia-live'` | The path given to `MapInertiaLiveHub`. |
| `throttle` | `1000` | Inertia's shortest time between two reloads of a prop, in milliseconds (a prop's `.Throttle(...)` wins). |
| `pauseWhenHidden` | Inertia's default | Holds reloads while the tab is hidden. |
| `idleTimeout` | unset | Stops the connection this many milliseconds after the last channel is left. Unset keeps it open. |
| `configure` | none | `(builder) => builder`, to set SignalR transports, logging or an access token factory. |
| `onConnection` | none | Called with the `HubConnection` once it is created. |

The transport reconnects on its own. After a reconnect it joins every channel again before it reports the connection, and Inertia
then reloads every live prop once, so events missed while offline are caught up.

## Channel authorization

| Channel | Who may join |
|---|---|
| `Public` | Anyone who can reach the hub. Set `o.AuthorizePublicChannels = true` to ask the authorizer about these too. |
| `Private`, `Presence`, `EncryptedPrivate` | Only connections the authorizer allows. Without an authorizer, nobody. |

A delegate (`o.Authorize((context, channel, ct) => ...)`) suits a one-line rule. For services, implement `ILiveChannelAuthorizer`
and register it with `o.Authorize<OrderChannelAuthorizer>()`; it is created per subscription with constructor injection:

```csharp
public sealed class OrderChannelAuthorizer(AppDb db) : ILiveChannelAuthorizer
{
    public async ValueTask<bool> AuthorizeAsync(HubCallerContext context, LiveChannel channel, CancellationToken cancellationToken) =>
        channel.Name.StartsWith("orders.", StringComparison.Ordinal)
        && int.TryParse(channel.Name.AsSpan("orders.".Length), out var id)
        && await db.Orders.AnyAsync(o => o.Id == id && o.OwnerId == context.UserIdentifier, cancellationToken);
}
```

`HubCallerContext` carries `User`, `UserIdentifier` and `GetHttpContext()`. A refused or unknown channel returns `false` to the client,
which logs a warning; the server logs it at Debug level.

SignalR has no Pusher-style presence member lists or end-to-end encryption: `Presence` and `EncryptedPrivate` channels are authorized
like private ones and carry the same payloads.

## How it maps

- One hub for every channel. A channel is a SignalR group named `"{type}:{name}"` (`private:orders.1`).
- The client calls `Subscribe(type, name)` (returns `bool`) and `Unsubscribe(type, name)`; events arrive on the client method
  `inertia(channelKey, payload)`, where the payload is the [broadcast envelope](https://github.com/adzamkomladev/Inertia.Net/blob/main/docs/protocol.md#broadcast-payload).
- The client's `X-Socket-Id` is its SignalR connection id. `exceptSocketId` sends to the group except that connection.
- `AddSignalR()` here is additive: your own hubs and `services.AddSignalR()` settings keep working.

## Scale-out

Groups are per server unless SignalR shares them. Behind more than one instance, add a backplane or Azure SignalR Service to the
same SignalR registration and publish as before:

```csharp
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnectionString);   // Microsoft.AspNetCore.SignalR.StackExchangeRedis
// or: builder.Services.AddSignalR().AddAzureSignalR();                      // Microsoft.Azure.SignalR
```

Without one, use sticky sessions and expect a publish on one instance to reach only the clients connected to it.

## Security

By default a connection may subscribe to 64 channels (`MaxSubscriptionsPerConnection`), and channel names may be 200 characters (`MaxChannelNameLength`). Configure trusted forwarded headers behind a proxy so origin checks see the external scheme and host. Authorization occurs when subscribing and again on reconnect; changing permissions does not evict an existing subscription.

- **Origin check.** Browsers do not apply CORS to WebSockets, and the hub authenticates with the app's cookie. A request whose `Origin`
  is neither the app's own nor in `o.AllowedOrigins` gets 403. That check stands in for CSRF protection, so `UseInertiaAntiforgery()`
  does not validate the hub's requests.
- **Authentication expiry.** Connections close when their authentication expires (`CloseOnAuthenticationExpiration`); the client
  reconnects, and each channel is authorized again.
- **Same values for everyone.** Every subscriber of a channel receives the same published values. When a value depends on the user,
  publish without values and let each client reload its own.
- **`X-Socket-Id` is client-supplied.** It is only ever used to leave a connection out, never to identify a user.

## Native AOT

The package is annotated `IsAotCompatible` and covered by the repository's AOT smoke test. The hub is untyped and its JSON metadata is
source-generated (inserted into the SignalR JSON protocol options). If your app adds hubs of its own, keep them untyped too: SignalR
does not support strongly typed hubs (`Hub<T>`) under Native AOT.

## Other transports

The page metadata and the payload do not depend on SignalR. For a Pusher-protocol server (Reverb, Soketi, Ably, Pusher), use the
client's built-in `pusher()` or `echo()` transport and implement `IInertiaLiveBroadcaster` with your server's publish API instead of
this package.

Documentation: <https://github.com/adzamkomladev/Inertia.Net#live-props>
