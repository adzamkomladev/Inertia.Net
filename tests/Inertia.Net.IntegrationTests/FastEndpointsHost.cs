using FastEndpoints;
using FluentValidation;
using Inertia.Net.FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Void = FastEndpoints.Void;

namespace Inertia.Net.IntegrationTests;

/// <summary>
/// The FastEndpoints conformance host. Only the endpoints in <see cref="FastEndpointsRoutes"/> are discovered, so other test
/// types (MVC controllers, the Minimal API host) are never picked up.
/// </summary>
public sealed class FastEndpointsHost : IConformanceHost
{
    // FastEndpoints discovers endpoints once per process (static state), so the extra routes that FastEndpointsTests adds
    // under /fe are discovered here as well.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<FastEndpointsTests.HandlerCalls>();
        services.AddFastEndpoints(o => o.Filter = t =>
            t.Namespace == typeof(FastEndpointsRoutes.PageEndpoint).Namespace || t.DeclaringType == typeof(FastEndpointsTests));
    }

    public static void MapRoutes(WebApplication app) =>
        app.UseFastEndpoints(c => c.UseInertia(ep => ep.AllowAnonymous()));
}

/// <summary>The route contract as FastEndpoints endpoints.</summary>
public static class FastEndpointsRoutes
{
    private static readonly Http[] Mutations = [Http.POST, Http.PUT, Http.PATCH, Http.DELETE];

    public sealed class PageEndpoint(ConformanceCounters counters) : EndpointWithoutRequest
    {
        public override void Configure() => Get("/page");

        public override async Task HandleAsync(CancellationToken ct)
        {
            counters.HitPage();
            await Send.InertiaAsync("Page", new InertiaProps { ["message"] = "hello" });
        }
    }

    public sealed class CafeEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/café");

        public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Page");
    }

    public sealed class PartialEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/partial");

        public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Partial", new InertiaProps
        {
            ["a"] = 1,
            ["b"] = Prop(() => 2),
            ["c"] = Always(3),
            ["d"] = Optional(() => 4),
        });
    }

    public sealed class DeferredEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/deferred");

        public override Task HandleAsync(CancellationToken ct) => Send.InertiaAsync("Deferred", new InertiaProps
        {
            ["a"] = 1,
            ["b"] = Defer(_ => Task.FromResult("b")),
            ["c"] = Defer(() => "c", "other"),
        });
    }

    public sealed class FormRequest
    {
        public string? Name { get; set; }
    }

    public sealed class FormValidator : Validator<FormRequest>
    {
        public FormValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    public sealed class FormEndpoint : Endpoint<FormRequest>
    {
        public override void Configure()
        {
            Verbs(Mutations);
            Routes("/form");
        }

        public override Task HandleAsync(FormRequest req, CancellationToken ct)
        {
            HttpContext.Inertia().Flash("success", $"Saved {req.Name}");
            return Send.RedirectAsync("/page");
        }
    }

    public sealed class RedirectEndpoint : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Verbs(Mutations);
            Routes("/redirect");
        }

        public override Task HandleAsync(CancellationToken ct) => Send.RedirectAsync("/page");
    }

    public sealed class NRequest
    {
        public int N { get; set; }
    }

    public sealed class FlashEndpoint : Endpoint<NRequest>
    {
        public override void Configure() => Post("/flash/{n}");

        public override Task HandleAsync(NRequest req, CancellationToken ct)
        {
            HttpContext.Inertia().Flash("n", req.N);
            return Send.RedirectAsync($"/isolated/{req.N}");
        }
    }

    public sealed class IsolatedEndpoint : Endpoint<NRequest>
    {
        public override void Configure() => Get("/isolated/{n}");

        public override Task HandleAsync(NRequest req, CancellationToken ct)
        {
            HttpContext.Inertia().Share("shared", req.N).EncryptHistory(req.N % 2 == 0);
            return Send.InertiaAsync("Isolated", new InertiaProps
            {
                ["n"] = Prop(async () =>
                {
                    await Task.Yield();
                    return req.N;
                }),
            });
        }
    }

    public sealed class FragmentRedirectEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/fragment-redirect");

        public override Task HandleAsync(CancellationToken ct) => Send.RedirectAsync("/page#section");
    }

    public sealed class ExternalEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/external");

        public override Task HandleAsync(CancellationToken ct) => Send.InertiaLocationAsync("https://external.example/path");
    }

    public sealed class EmptyEndpoint : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Verbs(Http.GET, Http.POST, Http.PUT, Http.PATCH, Http.DELETE);
            Routes("/empty");
        }

        public override Task HandleAsync(CancellationToken ct) => Send.ResultAsync(Results.Ok());
    }

    public sealed class ClearHistoryEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/clear-history");

        public override Task HandleAsync(CancellationToken ct)
        {
            HttpContext.Inertia().ClearHistory();
            return Send.RedirectAsync("/page");
        }
    }

    public sealed class PreserveFragmentEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/preserve-fragment");

        public override Task HandleAsync(CancellationToken ct)
        {
            HttpContext.Inertia().PreserveFragment();
            return Send.RedirectAsync("/page");
        }
    }

    public sealed class EncryptEndpoint : EndpointWithoutRequest
    {
        public override void Configure() => Get("/encrypt");

        public override Task HandleAsync(CancellationToken ct)
        {
            HttpContext.Inertia().EncryptHistory();
            return Send.InertiaAsync("Page");
        }
    }

    /// <summary>Returns the result from <c>ExecuteAsync</c> instead of using <c>Send</c>.</summary>
    public sealed class ErrorPageEndpoint : EndpointWithoutRequest<InertiaResult>
    {
        public override void Configure() => Get("/error-page");

        public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) =>
            Task.FromResult(Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404));
    }
}
