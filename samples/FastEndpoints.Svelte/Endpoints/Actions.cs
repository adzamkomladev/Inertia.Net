using FastEndpoints;
using FluentValidation;
using Inertia.Net.FastEndpoints;

namespace FastEndpointsSvelte.Endpoints;

// Flash through the scoped Inertia() context, then Send.InertiaBackAsync (a 303 for DELETE).
public sealed class DeleteUserEndpoint : Endpoint<DeleteUserRequest>
{
    public override void Configure()
    {
        Delete("/users/{id}");
        Description(b => b.WithName("users.destroy"));
    }

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
    public override void Configure()
    {
        Post("/contacts");
        Description(b => b.WithName("contacts.store"));
    }

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
    public override void Configure()
    {
        Post("/newsletter");
        Description(b => b.WithName("newsletter.store"));
    }

    public override Task<InertiaBackResult> ExecuteAsync(NewsletterRequest req, CancellationToken ct) =>
        Task.FromResult(Back("/contacts/create").WithFlash("success", "Subscribed"));
}

// The same validation for useHttp (an XHR without X-Inertia): failures are a 422 with { message, errors } that fills form.errors.
public sealed class CheckContactEndpoint : Endpoint<ContactRequest>
{
    public override void Configure()
    {
        Post("/contacts/check");
        Description(b => b.WithName("contacts.check"));
    }

    public override Task HandleAsync(ContactRequest req, CancellationToken ct) => Send.NoContentAsync();
}

public sealed class CreateLivePostEndpoint(IInertiaLive live) : EndpointWithoutRequest<InertiaBackResult>
{
    public override void Configure()
    {
        Post("/live/posts");
        Description(b => b.WithName("live.posts.store"));
    }

    public override async Task<InertiaBackResult> ExecuteAsync(CancellationToken ct)
    {
        AppState.AddLivePost();
        // The sender has the new post from this visit's response: leave its connection out.
        await live.PublishAsync(SampleEvents.PostCreated, exceptSocketId: HttpContext.InertiaRequest().SocketId);
        return Back("/live");
    }
}
