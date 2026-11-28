# Inertia.Net.Templates

`dotnet new` templates for [Inertia.js](https://inertiajs.com) v3 apps on ASP.NET Core with [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net).

```sh
dotnet new install Inertia.Net.Templates
dotnet new inertia -n MyApp                       # React (default)
dotnet new inertia -n MyApp --framework vue       # or svelte
dotnet new inertia -n MyApp --auth                # with cookie sign-in
cd MyApp
dotnet watch                                      # the app and Vite: http://localhost:5180
dotnet new inertia-page --component Users/Index   # add a page record and its page file (--framework vue|svelte)
```

The app is a Minimal API with a typed Todos page: validation that redirects back with errors, flash messages, a deferred prop,
generated TypeScript types for the pages and shared props, CSRF protection, compression, immutable caching of the Vite assets,
chunk prefetching and the Inertia DevTools in Development. Use your package manager to install dependencies and run the `build` script for production. In Development `dotnet watch` also starts
the `dev` script (`o.Vite.StartDevServer = true`) and stops it with the app. npm, Bun, pnpm and Yarn are supported. See the [starter README](content/common/README.md) for commands.

The client is linted with [oxlint](https://oxc.rs) and formatted with oxfmt (`lint`, `format` and `format:check` scripts). The
React starter type-checks with TypeScript 7 (`tsc`). Svelte type-checks with TypeScript 7 through `svelte-check --tsgo`, which also
needs TypeScript 6 installed for its own use. Vue (`vue-tsc`) uses TypeScript 6. Outside
Development the app redirects to HTTPS and sends HSTS.

For [live props](https://github.com/adzamkomladev/Inertia.Net#live-props) the app ships two client transports. For SignalR
(`ClientApp/lib/inertia-signalr.ts`), add `Inertia.Net.Live.SignalR`, call `AddInertiaLive().AddSignalR()` and `MapInertiaLiveHub()`,
and uncomment `live: signalr()` in `ClientApp/app`. For Server-Sent Events from one app instance (`ClientApp/lib/inertia-sse.ts`),
add `Inertia.Net.Live.Sse`, call `AddInertiaLive().AddSse()` and `MapInertiaLiveSse()`, and use `live: sse()` instead. `ClientApp/lib/inertia-live-reload.ts` reloads `Scroll` and `Merge` props, which cannot be live,
when an event arrives.

With `--auth` the app also has cookie sign-in: a `/login` page (demo@example.com / password, in Development only) whose form checks fields with
Precognition, a protected `/dashboard`, logout, the signed-in user shared as `auth.user`, and a sign-in rate limit that sends the
form back with a flash message. Inertia visits to a protected page are redirected to the login page (`UseInertiaRedirects()`).
Replace the in-memory `UserStore` with ASP.NET Core Identity or your user database.

`dotnet new inertia-page --component Users/Index` writes `UsersIndexPage.cs` (a page record with `Component => "Users/Index"`) and
`ClientApp/pages/Users/Index.tsx` (`.vue` or `.svelte` with `--framework`), typed with `InertiaPages['Users/Index']`. Run it in the
project directory after the first restore: the record's namespace is the project's root namespace. Then add
`[JsonSerializable(typeof(UsersIndexPage))]` to `AppJsonContext` in `Program.cs`, so the page renders and gets its type.
