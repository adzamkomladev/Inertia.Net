using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Inertia.Net;

/// <summary>
/// Precognition (live validation) as the laravel-precognition client expects it: the request carries <c>Precognition: true</c> and
/// optionally <c>Precognition-Validate-Only: a,b.c</c>; precognitive responses carry <c>Precognition: true</c> (the client throws without it)
/// and <c>Vary: Precognition</c>; valid is <c>204</c> + <c>Precognition-Success: true</c>, invalid is <c>422</c> with
/// <c>{ "message", "errors": { field: [messages] } }</c>. The handler never runs.
/// </summary>
public static class InertiaPrecognition
{
    /// <summary>The request and response header identifying a precognitive validation request.</summary>
    public const string Header = "Precognition";
    /// <summary>The request header listing the client field paths whose errors should be returned.</summary>
    public const string ValidateOnlyHeader = "Precognition-Validate-Only";
    /// <summary>The response header indicating that precognitive validation succeeded.</summary>
    public const string SuccessHeader = "Precognition-Success";

    /// <summary>
    /// Returns whether the request carries <c>Precognition: true</c>. For a separate validation rate-limit bucket, also check
    /// the endpoint's <see cref="InertiaPrecognitionMetadata"/>; the header alone does not guarantee that the handler is skipped.
    /// </summary>
    public static bool IsPrecognitive(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Equals(request.Headers[Header], "true", StringComparison.OrdinalIgnoreCase);
    }

    internal static void AppendVary(IHeaderDictionary headers) => headers.Vary = StringValues.Concat(headers.Vary, Header);

    /// <summary>The response for a precognitive request; <paramref name="errors"/> use CLR member paths, keyed to JSON names here.</summary>
    internal static IResult Result(HttpContext httpContext, Dictionary<string, string[]>? errors, JsonNamingPolicy? naming, JsonTypeInfo? schema = null)
    {
        if (errors is not { Count: > 0 })
        {
            return new PrecognitionResult(null);
        }

        errors = InertiaValidationFilter.ToClientKeys(errors, naming, schema);
        var only = httpContext.Request.Headers[ValidateOnlyHeader].ToString();
        if (only.Length > 0)
        {
            var fields = only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(f => InertiaValidationFilter.ToClientPath(f, null)).ToHashSet(StringComparer.Ordinal);
            var lookup = fields.GetAlternateLookup<ReadOnlySpan<char>>();
            errors = errors
                .Where(e => Matches(e.Key, lookup))
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        }

        return new PrecognitionResult(errors.Count == 0 ? null : errors);
    }

    // A field matches itself and, when it names an object or array, everything below it: the key, or a prefix of it ending before a
    // '.' or '['. One lookup per prefix, so a long header costs no more per error than a short one.
    private static bool Matches(string key, HashSet<string>.AlternateLookup<ReadOnlySpan<char>> fields)
    {
        if (fields.Contains(key)) return true;
        for (var i = 0; i < key.Length; i++)
        {
            var next = key.AsSpan(i).IndexOfAny(PathSeparators);
            if (next < 0) break;
            i += next;
            if (i > 0 && fields.Contains(key.AsSpan(0, i))) return true;
        }

        return false;
    }

    private static readonly SearchValues<char> PathSeparators = SearchValues.Create(".[");

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
