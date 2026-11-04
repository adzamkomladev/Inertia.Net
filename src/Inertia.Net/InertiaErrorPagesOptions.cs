using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Controls rendering of Inertia pages for exceptions and empty error responses.</summary>
public sealed class InertiaErrorPagesOptions
{
    /// <summary>The fallback error page component. Defaults to <c>Error</c>.</summary>
    public string Component { get; set; } = "Error";

    /// <summary>Optional component overrides for status codes in <see cref="StatusCodes"/>.</summary>
    public Dictionary<int, string> Components { get; } = [];

    /// <summary>
    /// Error status codes rendered as pages. Defaults to 403, 404, 500 and 503.
    /// Protocol responses 409 and 419 are always left unchanged.
    /// </summary>
    public HashSet<int> StatusCodes { get; set; } = [403, 404, 500, 503];

    /// <summary>Enables error pages in Development. Defaults to false, preserving developer error responses.</summary>
    public bool EnabledInDevelopment { get; set; }

    /// <summary>
    /// Optional extra props. Receives the exception for exception responses, otherwise null.
    /// Avoid exposing exception details or other secrets to clients.
    /// </summary>
    public Func<HttpContext, int, Exception?, InertiaProps>? Props { get; set; }
}

internal sealed class InertiaErrorPagesOptionsValidator : IValidateOptions<InertiaErrorPagesOptions>
{
    public ValidateOptionsResult Validate(string? name, InertiaErrorPagesOptions options)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(options.Component)) errors.Add("Inertia error pages Component must not be empty.");
        if (options.StatusCodes is not { Count: > 0 } || options.StatusCodes.Any(status => status is < 400 or > 599))
            errors.Add("Inertia error pages StatusCodes must contain error status codes between 400 and 599.");
        foreach (var (status, component) in options.Components)
        {
            if (options.StatusCodes?.Contains(status) != true) errors.Add($"Inertia error pages Components key {status} must be in StatusCodes.");
            if (string.IsNullOrWhiteSpace(component)) errors.Add($"Inertia error pages component for {status} must not be empty.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
