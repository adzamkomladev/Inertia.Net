// FastEndpoints endpoints for the AOT smoke: discovered by FastEndpoints.Generator, bound through the generated ReflectionCache.
using FastEndpoints;
using FluentValidation;
using Inertia.Net.FastEndpoints;

// Not under Inertia.Net.*: the FastEndpoints generators emit type names that are not global::-qualified, and inside the
// Inertia.Net.AotSmoke namespace "Inertia" binds to the Inertia.Net.Inertia class.
namespace SmokeEndpoints;

public sealed class FePageEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/fe/page");
        AllowAnonymous();
    }

    public override Task HandleAsync(CancellationToken ct) =>
        Send.InertiaAsync("Fe/Page", new InertiaProps { ["framework"] = "FastEndpoints", ["lazy"] = Defer(() => 7) });
}

public sealed class ContactRequest
{
    public string? Name { get; set; }
}

public sealed class ContactValidator : Validator<ContactRequest>
{
    public ContactValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
}

public sealed class FeContactEndpoint : Endpoint<ContactRequest>
{
    public override void Configure()
    {
        Post("/fe/contacts");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ContactRequest req, CancellationToken ct)
    {
        HttpContext.Inertia().Flash("toast", $"Contact {req.Name}");
        await Send.RedirectAsync("/fe/page");
    }
}
