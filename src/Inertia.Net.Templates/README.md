# Inertia.Net.Templates

`dotnet new` templates for [Inertia.js](https://inertiajs.com) v3 apps on ASP.NET Core with [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net).

```sh
dotnet new install Inertia.Net.Templates
dotnet new inertia -n MyApp                       # React (default)
dotnet new inertia -n MyApp --framework vue       # or svelte
cd MyApp && npm install && npm run dev            # Vite, in one terminal
dotnet watch                                      # the app, in another: http://localhost:5180
```

The app is a Minimal API with a typed Todos page: validation that redirects back with errors, flash messages, a deferred prop,
generated TypeScript types for the pages and shared props, CSRF protection, compression, immutable caching of the Vite assets,
chunk prefetching and the Inertia DevTools in Development. `npm run build` builds the client for production.
