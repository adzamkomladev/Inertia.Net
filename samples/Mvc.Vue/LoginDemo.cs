using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Mvc.Vue;

/// <summary>
/// A validation demo; accepted submissions do not create an authentication session. Precognition (<c>WithInertiaPrecognition()</c>)
/// validates with the Minimal API validation (<c>AddValidation()</c>), so the login form is a Minimal API endpoint next to the controllers.
/// </summary>
internal static class LoginDemo
{
    private static int _submissions;
    private static int _generation;

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

    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("/login", () => Render(new LoginPage(Volatile.Read(ref _submissions)))).WithName("login");
        app.MapPost("/login", (LoginForm input) =>
        {
            Interlocked.Increment(ref _submissions);
            return Back().WithFlash("success", "Demo form submitted");
        }).WithInertiaPrecognition().RequireRateLimiting("login").WithName("login.store");
    }

    public static void Reset()
    {
        Interlocked.Exchange(ref _submissions, 0);
        Interlocked.Increment(ref _generation);
    }
}

public sealed class LoginForm
{
    [Required, EmailAddress]
    public string? Email { get; set; }

    [Required, MinLength(8)]
    public string? Password { get; set; }
}

/// <summary>The Login page's props; the record names its component, so <c>Render(new LoginPage(n))</c> needs no string.</summary>
public sealed record LoginPage(int Submissions) : IInertiaPage
{
    public static string Component => "Login";
}
