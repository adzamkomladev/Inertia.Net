# Inertia.Net.EntityFrameworkCore

Page EF Core queries with [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net#pagination). Install this package alongside an EF Core provider:

```sh
dotnet add package Inertia.Net.EntityFrameworkCore --version 1.6.2
```

The following uses an existing `AppDb` as `db`, with a `Posts` set. `request` is a `PageRequest`, `cursorRequest` is a `CursorRequest`, and `ct` is the request cancellation token.

```csharp
using Inertia.Net;
using Microsoft.EntityFrameworkCore;

var page = await db.Posts.OrderByDescending(p => p.Id)
    .ToPageAsync(request, ct: ct);
var cursorPage = await db.Posts
    .ToCursorPageAsync(p => p.CreatedAt, p => p.Id, cursorRequest,
        descending: true, ct: ct);

return Render("Feed", new InertiaProps { ["posts"] = Scroll(cursorPage) });
```

Each result contains the current group in `data`. Send a cursor marker back unchanged to request another group. `ToPageAsync` requires an ordered query and includes a count by default; `countTotal: false` skips the count. `ToCursorPageAsync` requires an unordered query and applies the key and optional tie-breaker ordering itself. Both reject a query that violates its ordering rule. The results can be returned as props or passed to `Scroll(...)` for infinite scroll. A bound request or `From(...)` retains the configured page or cursor parameter name; an explicit name passed to the helper takes precedence.

Choose a stable, fully unique ordering, adding a unique tie-breaker for duplicate first keys. Nullable keys are unsupported. Cursor key types are documented in [Pagination](https://github.com/adzamkomladev/Inertia.Net#cursors); translation and ordering depend on your EF provider, for example, SQLite needs a suitable value conversion to order `DateTimeOffset`.

This package is not marked Native AOT compatible. EF Core has its own compiled-model requirements; Native AOT apps can instead use the core delegate paginators with an AOT-compatible data source. See [Pagination](https://github.com/adzamkomladev/Inertia.Net#with-ef-core).
