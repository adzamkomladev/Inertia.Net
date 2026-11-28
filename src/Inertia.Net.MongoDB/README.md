# Inertia.Net.MongoDB

Pagination for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net) on MongoDB finds: the same `Page<T>` and `CursorPage<T>`
values as `Inertia.Net.EntityFrameworkCore`, built from `IFindFluent<T, T>`.

```csharp
["users"] = await users.Find(u => u.Active).SortBy(u => u.Name).ThenBy(u => u.Id)
    .ToPageAsync(PageRequest.From(http.Request.Query), ct: ct),
["feed"] = Scroll(await posts.Find(p => p.Published)
    .ToCursorPageAsync(p => p.CreatedAt, p => p.Id, CursorRequest.From(http), descending: true, ct: ct)),
```

- `ToPageAsync` needs a sorted find (skip and limit are replaced) and counts the filter with `CountDocuments` unless
  `countTotal: false`, or the page already gives the total (a first page with no more rows, a last page with rows).
- `ToCursorPageAsync` needs an unsorted find: it adds the keyset condition to the find's filter and sorts by the key (and
  tie-breaker). Keys may be `ObjectId` (held in the cursor as its 24-character string), `int`, `long`, `string`, `Guid`,
  `DateTime`, `DateTimeOffset`, `decimal`, `double` or `bool`. A tampered or unparseable cursor starts at the first page.
- Not Native AOT compatible (the MongoDB driver is not).

### Project MongoDB lists into a DTO

Use `ToProjectedPageAsync` after `Project` when the view needs only a few fields. The sort and count still apply to the source query, and the page holds the projected type.

```csharp
var page = await articles.Find(a => a.Published)
    .SortBy(a => a.Id)
    .Project(a => new ArticleSummary(a.Id, a.Title))
    .ToProjectedPageAsync(request, ct: ct);
```
