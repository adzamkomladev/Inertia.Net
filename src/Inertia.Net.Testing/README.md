# Inertia.Net.Testing

Test helpers for [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net): Laravel-style fluent assertions over Inertia responses, for any test framework. It parses JSON responses and the first-visit HTML `<script data-page>`, and throws `InertiaAssertionException` when an assertion fails.

```sh
dotnet add package Inertia.Net.Testing
```

```csharp
using Inertia.Net.Testing;

using var response = await client.InertiaGetAsync("/users");          // sends X-Inertia

await response.AssertInertiaAsync(page => page
    .Component("Users/Index")
    .Has("users", 3, first => first.Where("name", "Ann").Etc())
    .Missing("secret")
    .HasFlash("toast", "Welcome"));

// Partial reloads and deferred props:
var page = await response.GetInertiaPageAsync();
var deferred = await page.LoadDeferredPropsAsync(client, "sidebar");
var only = await page.ReloadOnlyAsync(client, "users");
```

For form redirects, keep cookies and disable automatic redirects on the client. This example uses the sample's contact form:

```csharp
client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
using var form = await client.GetAsync("/contacts/create");
using var submitted = await client.InertiaRequestAsync(HttpMethod.Post, "/contacts",
    JsonContent.Create(new { name = "", email = "" }), configure: request =>
    {
        request.Headers.Referrer = new Uri(client.BaseAddress!, "/contacts/create");
        request.WithXsrfToken(form);
    });
Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
using var followed = await client.GetAsync("/contacts/create");
await followed.AssertInertiaAsync(p => p.HasErrors().HasError("name").HasError("email"));
```

This example uses `System.Net` and `System.Net.Http.Json`. `GetXsrfToken()` reads and URL-decodes the page's token;
`WithXsrfToken()` accepts either that token or the page response, plus optional custom cookie/header names. The client's
cookie container must retain the paired antiforgery cookie too.

`HasErrors()` and `HasError(field, message)` accept an optional named bag; `HasErrorBag()` checks its presence.
Pass client field names such as `items.0.email` directly. Assertions support string and array messages.
Default-bag errors are flat (`errors.email`); a named bag is nested (`errors.createUser.email`).

For infinite scroll, `new AssertableInertia(page).HasScroll("posts")` checks for scroll metadata; the overload with current and next page values checks those too.

`await page.LoadNextPageAsync(client, "posts")` and `LoadPreviousPageAsync` replay a partial request with the metadata's page name and the correct merge intent.

Error pages: non-2xx Inertia responses are parsed too; `.Status(404)` asserts the HTTP status alongside `.Component("Error").Where("status", 404)`.

Live props: `page.LiveProps` holds the `liveProps` metadata, and `HasLive("order")` or
`HasLive("order", "private:orders.1", "OrderUpdated")` (channel as `"{type}:{name}"`) assert it.

Nested scopes fail on properties you did not assert unless you call `Etc()`. Call `Interacted()` to enforce the same at the root.

Documentation: <https://github.com/adzamkomladev/Inertia.Net#testing>
