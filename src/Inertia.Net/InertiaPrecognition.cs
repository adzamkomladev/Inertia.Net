using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Inertia.Net;

/// <summary>
/// Precognition (live validation) as the laravel-precognition client expects it: the request carries <c>Precognition: true</c> and
/// optionally <c>Precognition-Validate-Only: a,b.c</c>; every response carries <c>Precognition: true</c> (the client throws without it)
/// and <c>Vary: Precognition</c>; valid is <c>204</c> + <c>Precognition-Success: true</c>, invalid is <c>422</c> with
/// <c>{ "message", "errors": { field: [messages] } }</c>. The handler never runs.
/// </summary>
internal static class InertiaPrecognition
{
    public const string Header = "Precognition";
    public const string ValidateOnlyHeader = "Precognition-Validate-Only";
    public const string SuccessHeader = "Precognition-Success";

    public static bool IsPrecognitive(HttpRequest request) =>
        string.Equals(request.Headers[Header], "true", StringComparison.OrdinalIgnoreCase);

    public static void AppendVary(IHeaderDictionary headers) => headers.Vary = StringValues.Concat(headers.Vary, Header);

    /// <summary>The response for a precognitive request; <paramref name="errors"/> use CLR member paths, keyed to JSON names here.</summary>
    public static IResult Result(HttpContext httpContext, Dictionary<string, string[]>? errors, JsonNamingPolicy? naming)
    {
        if (errors is not { Count: > 0 })
        {
            return new PrecognitionResult(null);
        }

        errors = InertiaValidationFilter.ToClientKeys(errors, naming);
        var only = httpContext.Request.Headers[ValidateOnlyHeader].ToString();
        if (only.Length > 0)
        {
            var fields = only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            // A field matches itself and, when it names an object or array, everything below it.
            errors = errors
                .Where(e => fields.Any(f => e.Key == f || e.Key.StartsWith(f + ".", StringComparison.Ordinal) || e.Key.StartsWith(f + "[", StringComparison.Ordinal)))
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        }

        return new PrecognitionResult(errors.Count == 0 ? null : errors);
    }

    private sealed class PrecognitionResult(Dictionary<string, string[]>? errors) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.Headers[Header] = "true";
            AppendVary(response.Headers);
            if (errors is null)
            {
                response.StatusCode = StatusCodes.Status204NoContent;
                response.Headers[SuccessHeader] = "true";
                return;
            }

            response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            response.ContentType = "application/json";
            await using var writer = new Utf8JsonWriter(response.BodyWriter);
            writer.WriteStartObject();
            var first = errors.Values.First(v => v.Length > 0)[0];
            writer.WriteString("message", errors.Count == 1 ? first : $"{first} (and {errors.Count - 1} more error{(errors.Count == 2 ? "" : "s")})");
            writer.WriteStartObject("errors");
            foreach (var (field, messages) in errors)
            {
                writer.WriteStartArray(field);
                foreach (var message in messages)
                {
                    writer.WriteStringValue(message);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}
