# Inertia.Net.Testing

Use the 1.1 packages together and update response expectations for whole-array reloads, corrected JSON contracts and dotted validation indices. See [migration guidance](https://github.com/adzamkomladev/Inertia.Net/blob/main/docs/migrating-to-1.1.md).

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

Nested scopes fail on properties you did not assert unless you call `Etc()`. Call `Interacted()` to enforce the same at the root.

Documentation: <https://github.com/adzamkomladev/Inertia.Net#testing>
