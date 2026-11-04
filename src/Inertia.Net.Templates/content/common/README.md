# InertiaStarter

An ASP.NET Core Minimal API app with an Inertia.js v3 client, built on [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net).

## Run it

Install dependencies and start Vite with the package manager you use for your app. npm, Bun, pnpm and Yarn are supported:

| Package manager | Install dependencies | Start Vite | Build assets |
|---|---|---|---|
| npm | `npm install` | `npm run dev` | `npm run build` |
| Bun | `bun install` | `bun run dev` | `bun run build` |
| pnpm | `pnpm install` | `pnpm run dev` | `pnpm run build` |
| Yarn | `yarn install` | `yarn run dev` | `yarn run build` |

Keep Vite running in one terminal. Start ASP.NET Core in a second terminal:

```sh
dotnet watch       # http://localhost:5180
```

While Vite runs it writes `wwwroot/hot`, and the app serves the pages through it. For production, run the build command from
your row to write `wwwroot/build`, then publish with `dotnet publish -c Release`.

## Where things are

| Path | What |
|---|---|
| `Program.cs` | Routes, props, validation and the Inertia.Net setup |
| `app.html` | The HTML shell of the first visit (`@vite`, `@inertiaHead`, `@inertia`) |
| `ClientApp/pages` | One component per page: `Render("Todos/Index")` renders `ClientApp/pages/Todos/Index` |
| `ClientApp/lib/inertia-signalr.ts` | The client transport for live props over SignalR (off until you enable it) |
| `ClientApp/types/inertia.d.ts` | Types for the pages, shared props and flash data, rewritten from the C# types when the app starts in Development |

The template disables reflection-based JSON serialization. Use named DTOs and register their source-generated JSON metadata; anonymous values require reflection-enabled serialization.

## Next steps

- Add a page: a component under `ClientApp/pages`, a route that calls `Render`, and `o.TypeScript.Page<TProps>(...)` for its types.
- Install the Inertia DevTools browser extension: the app records requests for it in Development.
- Live props (props that refresh when the server broadcasts an event): add the `Inertia.Net.Live.SignalR` package, call
  `builder.Services.AddInertiaLive().AddSignalR()` and `app.MapInertiaLiveHub()`, then uncomment `live: signalr()` in `ClientApp/app`.
- Server-side rendering, once props, infinite scroll and the rest are in the Inertia.Net README.
