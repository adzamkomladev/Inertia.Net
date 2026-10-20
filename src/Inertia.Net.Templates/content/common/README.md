# InertiaStarter

An ASP.NET Core Minimal API app with an Inertia.js v3 client, built on [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net).

## Run it

```sh
npm install
npm run dev        # the Vite dev server (hot reload); keep it running
dotnet watch       # the app, in a second terminal: http://localhost:5180
```

While the Vite dev server runs it writes `wwwroot/hot`, and the app serves the pages through it. Without it the app uses the
production build from `npm run build` (`wwwroot/build`).

## Where things are

| Path | What |
|---|---|
| `Program.cs` | Routes, props, validation and the Inertia.Net setup |
| `app.html` | The HTML shell of the first visit (`@vite`, `@inertiaHead`, `@inertia`) |
| `ClientApp/pages` | One component per page: `Render("Todos/Index")` renders `ClientApp/pages/Todos/Index` |
| `ClientApp/types/inertia.d.ts` | Types for the pages, shared props and flash data, rewritten from the C# types when the app starts in Development |

## Next steps

- Add a page: a component under `ClientApp/pages`, a route that calls `Render`, and `o.TypeScript.Page<TProps>(...)` for its types.
- Install the Inertia DevTools browser extension: the app records requests for it in Development.
- Server-side rendering, once props, infinite scroll and the rest are in the Inertia.Net README.
