using FastEndpoints;
using FluentValidation;
using Inertia.Net.FastEndpoints;

namespace FastEndpointsSvelte.Endpoints;

// Flash through the scoped Inertia() context, then Send.InertiaBackAsync (a 303 for DELETE).
public sealed class DeleteUserEndpoint : Endpoint<DeleteUserRequest>
{
    public override void Configure() => Delete("/users/{id}");

    public override async Task HandleAsync(DeleteUserRequest req, CancellationToken ct)
    {
        AppState.Users.RemoveAll(u => u.Id == req.Id);
        HttpContext.Inertia().Flash("success", "User deleted");
        await Send.InertiaBackAsync("/users");
    }
}

public sealed class ContactValidator : Validator<ContactRequest>
{
    public ContactValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").EmailAddress().WithMessage("Enter a valid email address.");
    }
}

public sealed class CreateContactEndpoint : Endpoint<ContactRequest>
{
    public override void Configure() => Post("/contacts");

    public override async Task HandleAsync(ContactRequest req, CancellationToken ct)
    {
        HttpContext.Inertia().Flash("success", "Contact created");
        await Send.RedirectAsync("/");
    }
}

public sealed class NewsletterValidator : Validator<NewsletterRequest>
{
    public NewsletterValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.").EmailAddress().WithMessage("Enter a valid email address.");
}

// Flash with the result's own WithFlash, returned from ExecuteAsync.
public sealed class SubscribeEndpoint : Endpoint<NewsletterRequest, InertiaBackResult>
{
    public override void Configure() => Post("/newsletter");

    public override Task<InertiaBackResult> ExecuteAsync(NewsletterRequest req, CancellationToken ct) =>
        Task.FromResult(Back("/contacts/create").WithFlash("success", "Subscribed"));
}
