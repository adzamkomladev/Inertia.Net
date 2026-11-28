using System.Threading.RateLimiting;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;

namespace FastEndpointsSvelte;

/// <summary>A validation demo; accepted submissions do not create an authentication session.</summary>
internal static class LoginDemo
{
    private static int _submissions;
    private static int _generation;

    public static int Submissions => Volatile.Read(ref _submissions);

    public static void AddServices(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("login", http =>
            {
                var validation = http.GetEndpoint()?.Metadata.GetMetadata<InertiaPrecognitionMetadata>() is not null
                    && InertiaPrecognition.IsPrecognitive(http.Request);
                var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                // The generation lets the sample's E2E reset endpoint give each test fresh buckets.
                var generation = Volatile.Read(ref _generation);
                if (validation)
                {
                    return RateLimitPartition.GetSlidingWindowLimiter($"validation:{client}:{generation}", _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
                }

                return RateLimitPartition.GetFixedWindowLimiter($"submit:{client}:{generation}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            options.OnRejected = InertiaRateLimiting.OnRejected(); // Precognition: true on validation, the 429 error page on submit
        });
    }

    public static void Submitted() => Interlocked.Increment(ref _submissions);

    public static void Reset()
    {
        Interlocked.Exchange(ref _submissions, 0);
        Interlocked.Increment(ref _generation);
    }
}

public sealed class LoginForm
{
    public string? Email { get; set; }

    public string? Password { get; set; }
}

/// <summary>The Login page's props; the record names its component, so <c>Render(new LoginPage(n))</c> needs no string.</summary>
public sealed record LoginPage(int Submissions) : IInertiaPage
{
    public static string Component => "Login";
}

public sealed class LoginValidator : Validator<LoginForm>
{
    public LoginValidator()
    {
        // The same messages as the React sample's data annotations.
        RuleFor(x => x.Email).NotEmpty().WithMessage("The Email field is required.")
            .EmailAddress().WithMessage("The Email field is not a valid e-mail address.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("The Password field is required.")
            .MinimumLength(8).WithMessage("The field Password must be a string or array type with a minimum length of '8'.");
    }
}

public sealed class LoginPageEndpoint : EndpointWithoutRequest<InertiaResult>
{
    public override void Configure()
    {
        Get("/login");
        Description(b => b.WithName("login"));
    }

    public override Task<InertiaResult> ExecuteAsync(CancellationToken ct) => Task.FromResult(Render(new LoginPage(LoginDemo.Submissions)));
}

// c.ConfigureInertia() answers Precognition requests after validation without running the handler; the metadata gives them their own bucket.
public sealed class LoginEndpoint : Endpoint<LoginForm, InertiaBackResult>
{
    public override void Configure()
    {
        Post("/login");
        Description(b => b.WithName("login.store"));
        Options(b => b.RequireRateLimiting("login").WithMetadata(new InertiaPrecognitionMetadata()));
    }

    public override Task<InertiaBackResult> ExecuteAsync(LoginForm req, CancellationToken ct)
    {
        LoginDemo.Submitted();
        return Task.FromResult(Back().WithFlash("success", "Demo form submitted"));
    }
}
