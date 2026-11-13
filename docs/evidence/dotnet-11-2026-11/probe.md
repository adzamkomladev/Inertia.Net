# .NET 11 probe (Phase 0), 2026-11-13

Base: `main` at `785ec02` (v1.7.1). Windows 11, x64.

## Toolchain

- Baseline SDK: 10.0.404 (system install). Runtimes: Microsoft.AspNetCore.App 10.0.11, 10.0.15.
- .NET 11 SDK 11.0.100 installed user-locally with `dotnet-install.ps1 -Channel 11.0`, plus the 10.0.15 runtimes next to it.
  `--list-runtimes`: Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.15 and 11.0.0.

## Baseline on SDK 10 (`net10.0` only)

- `dotnet build Inertia.Net.slnx -c Release -warnaserror`: succeeded, 0 warnings.
- `dotnet test --solution Inertia.Net.slnx -c Release --no-build`: 1521 total, 1509 passed, 0 failed, 12 skipped
  (the `SsrNodeProcessTests` that need `node` on `PATH`).

## Roll-forward probe (net10.0 binaries on the .NET 11 runtime)

`DOTNET_ROLL_FORWARD=LatestMajor`, `DOTNET_ROOT` = the SDK 11 install, test executables run directly.

| Assembly | Total | Failed |
| --- | --- | --- |
| Inertia.Net.Tests | 766 | 0 (12 skipped) |
| Inertia.Net.IntegrationTests | 657 | 552 |
| Inertia.Net.Testing.Tests | 62 | 0 |
| Inertia.Net.EntityFrameworkCore.Tests | 36 | 0 |

Almost every failure is in the test assembly's own source-generated validation resolver, compiled by the .NET 10 SDK:
`Could not load type 'Microsoft.Extensions.Validation.ValidatablePropertyInfo' / 'ValidatableTypeInfo'` and `Method
'TryGetValidatableTypeInfo' ... does not have an implementation` (318 `ReflectionTypeLoadException` from MVC application part scanning,
225 `TypeLoadException`). Any `net10.0` app that calls `AddValidation()` breaks this way when rolled forward, with or without Inertia.Net.

## Second probe: tests compiled for net11.0 against the unchanged net10.0 libraries

What a .NET 11 app referencing Inertia.Net 1.7.1 gets. Built with SDK 11, `src/` held to `net10.0`.

| Assembly | Total | Failed |
| --- | --- | --- |
| Inertia.Net.Tests | 766 | 1 (timing-sensitive `SsrCacheTests`, passed on rerun) |
| Inertia.Net.IntegrationTests | 657 | 332 |
| Inertia.Net.Testing.Tests | 62 | 0 |
| Inertia.Net.EntityFrameworkCore.Tests | 36 | 0 |

326 of the integration failures: `TypeLoadException: Could not load type 'Microsoft.Extensions.Validation.IValidatableInfo' from
assembly 'Microsoft.Extensions.Validation, Version=11.0.0.0'` (directly or via `ReflectionTypeLoadException`), raised by
`InertiaValidation`. **Inertia.Net 1.7.1 is broken on .NET 11**; the 1.8.0 changelog says so.

## The .NET 11 validation API (reflected from Microsoft.Extensions.Validation 11.0.0)

- `ValidationOptions.TryGetValidatableParameterInfo(ParameterInfo, out IValidatableParameterInfo)` (was `out IValidatableInfo`).
- `IValidatableParameterInfo.ValidateAsync(object?, ValidateContext, CancellationToken)`.
- `ValidateContext { ServiceProvider, ValidationOptions, CurrentValidationPath, CurrentDepth }`; no `ValidationContext` property.
- `ValidateContext.ValidationErrors` is `IReadOnlyDictionary<string, IReadOnlyList<ValidationError>>` (was `Dictionary<string, string[]>`);
  `ValidationError { Name, Path, ErrorMessage, Container }`.

## Found in Phase 2

- On a `WebApplication.CreateBuilder` host, .NET 11's automatic CSRF middleware sets a failed `IAntiforgeryValidationFeature` for a
  cross-site unsafe request. `UseInertiaAntiforgery()` then called `IAntiforgery.ValidateRequestAsync`, whose form read throws
  `InvalidOperationException: This form is being accessed with a failed antiforgery validation`: a 500 instead of `419`. Fixed by
  clearing the verdict before the token check on `net11.0` (`AutomaticCsrfTests`).
- FastEndpoints 9 made `FastEndpoints.Void` a struct; the three `Task<Void>` signatures in `Inertia.Net.FastEndpoints` are declared per
  framework in `PublicAPI/<tfm>/PublicAPI.Shipped.txt`.
- .NET 11 adds `JsonSerializerOptions.GetTypeInfo<T>()`, so CA2263 fires on two `GetTypeInfo(typeof(...))` calls that `net10.0` also
  compiles; suppressed locally with a reason.
- AOT smoke (WSL Ubuntu, linux-x64, SDK 11.0.100): `bash eng/aot-smoke.sh` and `TFM=net11.0 bash eng/aot-smoke.sh` both pass with no IL2xxx/IL3xxx warnings. The first run exposed that restore ignores a `-p:TargetFramework` override, so the script now passes `-p:AotSmokeTargetFramework`, which the AotSmoke project reads.
