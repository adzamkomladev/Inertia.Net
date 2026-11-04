# Inertia.Net.Templates

`dotnet new` templates for [Inertia.js](https://inertiajs.com) v3 apps on ASP.NET Core with [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net).

```sh
dotnet new install Inertia.Net.Templates
dotnet new inertia -n MyApp                       # React (default)
dotnet new inertia -n MyApp --framework vue       # or svelte
cd MyApp
dotnet watch                                      # the app: http://localhost:5180
```

The app is a Minimal API with a typed Todos page: validation that redirects back with errors, flash messages, a deferred prop,
generated TypeScript types for the pages and shared props, CSRF protection, compression, immutable caching of the Vite assets,
chunk prefetching and the Inertia DevTools in Development. Use your package manager to install dependencies, run the `dev` script in another terminal, and run the `build` script for production. npm, Bun, pnpm and Yarn are supported. See the [starter README](content/common/README.md) for commands.

For [live props](https://github.com/adzamkomladev/Inertia.Net#live-props) the app ships the SignalR client transport
(`ClientApp/lib/inertia-signalr.ts`): add `Inertia.Net.Live.SignalR`, call `AddInertiaLive().AddSignalR()` and `MapInertiaLiveHub()`,
and uncomment `live: signalr()` in `ClientApp/app`.
