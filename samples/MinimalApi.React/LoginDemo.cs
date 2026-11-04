using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MinimalApi.React;

/// <summary>A validation demo; accepted submissions do not create an authentication session.</summary>
internal static class LoginDemo
{
    private static int _submissions;
    private static int _generation;

    public static void AddServices(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
            options.OnRejected = (context, _) =>
            {
                var http = context.HttpContext;
                if (http.GetEndpoint()?.Metadata.GetMetadata<InertiaPrecognitionMetadata>() is not null
                    && InertiaPrecognition.IsPrecognitive(http.Request))
                {
                    http.Response.Headers[InertiaPrecognition.Header] = "true";
                    http.Response.Headers.Append("Vary", InertiaPrecognition.Header);
                }

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    http.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
    }

    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("/login", () => Render("Login", new LoginPage(Volatile.Read(ref _submissions))));
        app.MapPost("/login", (LoginForm input) =>
        {
            Interlocked.Increment(ref _submissions);
            return Back().WithFlash("success", "Demo form submitted");
        }).WithInertiaPrecognition().RequireRateLimiting("login");
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

public sealed record LoginPage(int Submissions);
