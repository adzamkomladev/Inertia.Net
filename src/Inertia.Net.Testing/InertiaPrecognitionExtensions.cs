using System.Net;
using System.Text.Json;

namespace Inertia.Net.Testing;

/// <summary>Request and response helpers for Precognition live validation.</summary>
public static class InertiaPrecognitionExtensions
{
    private const string Header = "Precognition";
    private const string ValidateOnlyHeader = "Precognition-Validate-Only";
    private const string SuccessHeader = "Precognition-Success";

    /// <summary>Marks a request as precognitive. Pass client field paths to request validation errors for those fields only.</summary>
    public static HttpRequestMessage WithPrecognition(this HttpRequestMessage request, params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fields);
        request.Headers.Remove(Header);
        request.Headers.Add(Header, "true");
        request.Headers.Remove(ValidateOnlyHeader);
        if (fields.Length > 0) request.Headers.Add(ValidateOnlyHeader, string.Join(',', fields));
        return request;
    }

    /// <summary>Asserts that the endpoint handled Precognition and returned a successful 204 response.</summary>
    public static HttpResponseMessage AssertSuccessfulPrecognition(this HttpResponseMessage response)
    {
        AssertResponse(response, HttpStatusCode.NoContent);
        if (!HasTrueHeader(response, SuccessHeader))
        {
            throw new InertiaAssertionException($"Expected '{SuccessHeader}: true'.");
        }

        return response;
    }

    /// <summary>Asserts a Precognition 422 response has an error at the given client field path, optionally matching a specific message.</summary>
    public static async Task<HttpResponseMessage> AssertPrecognitionErrorAsync(this HttpResponseMessage response, string field,
        string? message = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        AssertResponse(response, HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object
                && errors.TryGetProperty(field, out var messages) && messages.ValueKind == JsonValueKind.Array
                && messages.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && (message is null || value.GetString() == message)))
            {
                return response;
            }
        }
        catch (JsonException)
        {
            throw new InertiaAssertionException("Expected the Precognition response to contain a JSON error object.");
        }

        throw new InertiaAssertionException(message is null
            ? $"Expected a Precognition error for '{field}'."
            : $"Expected a Precognition error for '{field}' with message '{message}'.");
    }

    private static bool HasTrueHeader(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) && values.SequenceEqual(["true"], StringComparer.OrdinalIgnoreCase);

    private static void AssertResponse(HttpResponseMessage response, HttpStatusCode status)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode != status || !HasTrueHeader(response, Header))
        {
            throw new InertiaAssertionException($"Expected HTTP {(int)status} with '{Header}: true', received HTTP {(int)response.StatusCode}.");
        }
    }
}
