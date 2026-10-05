using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Inertia.Net;

/// <summary>Inertia protocol header names.</summary>
public static class InertiaHeaders
{
    /// <summary><c>X-Inertia</c>: marks an Inertia request and a JSON page response.</summary>
    public const string Inertia = "X-Inertia";

    /// <summary><c>X-Inertia-Version</c>: the client's asset version.</summary>
    public const string Version = "X-Inertia-Version";

    /// <summary><c>X-Inertia-Location</c>: target of a full page visit (409 response).</summary>
    public const string Location = "X-Inertia-Location";

    /// <summary><c>X-Inertia-Redirect</c>: redirect target that contains a fragment (409 response).</summary>
    public const string Redirect = "X-Inertia-Redirect";

    /// <summary><c>X-Inertia-Partial-Component</c>: the component a partial reload targets.</summary>
    public const string PartialComponent = "X-Inertia-Partial-Component";

    /// <summary><c>X-Inertia-Partial-Data</c>: prop paths to include.</summary>
    public const string PartialData = "X-Inertia-Partial-Data";

    /// <summary><c>X-Inertia-Partial-Except</c>: prop paths to exclude.</summary>
    public const string PartialExcept = "X-Inertia-Partial-Except";

    /// <summary><c>X-Inertia-Reset</c>: prop paths whose merge state is reset.</summary>
    public const string Reset = "X-Inertia-Reset";

    /// <summary><c>X-Inertia-Error-Bag</c>: the error bag the request's errors belong to.</summary>
    public const string ErrorBag = "X-Inertia-Error-Bag";

    /// <summary><c>X-Inertia-Except-Once-Props</c>: once keys the client already holds.</summary>
    public const string ExceptOnceProps = "X-Inertia-Except-Once-Props";

    /// <summary><c>X-Inertia-Infinite-Scroll-Merge-Intent</c>: <c>append</c> or <c>prepend</c>.</summary>
    public const string InfiniteScrollMergeIntent = "X-Inertia-Infinite-Scroll-Merge-Intent";

    /// <summary><c>Purpose</c>: <c>prefetch</c> for prefetch requests.</summary>
    public const string Purpose = "Purpose";

    /// <summary><c>X-XSRF-TOKEN</c>: the antiforgery token the client copies from the <c>XSRF-TOKEN</c> cookie.</summary>
    public const string XsrfToken = "X-XSRF-TOKEN";

    /// <summary>Adds <c>X-Inertia</c> to <c>Vary</c> unless it is already listed.</summary>
    internal static void AppendVary(IHeaderDictionary headers)
    {
        var vary = headers.Vary;
        foreach (var value in vary)
        {
            var span = value.AsSpan();
            foreach (var range in span.Split(','))
            {
                if (span[range].Trim().Equals(Inertia, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        headers.Vary = StringValues.Concat(vary, Inertia);
    }
}

/// <summary>The Inertia request headers, parsed once. Missing or empty headers are null.</summary>
public readonly struct InertiaRequest
{
    /// <summary>Parses the Inertia headers.</summary>
    public InertiaRequest(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        IsInertia = !StringValues.IsNullOrEmpty(headers[InertiaHeaders.Inertia]);
        Version = NullIfEmpty(headers[InertiaHeaders.Version]);
        PartialComponent = NullIfEmpty(headers[InertiaHeaders.PartialComponent]);
        Only = ParseList(headers[InertiaHeaders.PartialData]);
        Except = ParseList(headers[InertiaHeaders.PartialExcept]);
        Reset = ParseList(headers[InertiaHeaders.Reset]);
        ErrorBag = NullIfEmpty(headers[InertiaHeaders.ErrorBag]);
        ExceptOnceProps = ParseList(headers[InertiaHeaders.ExceptOnceProps]);
        MergeIntentPrepend = string.Equals(headers[InertiaHeaders.InfiniteScrollMergeIntent], "prepend", StringComparison.Ordinal);
        IsPrefetch = string.Equals(headers[InertiaHeaders.Purpose], "prefetch", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The request carries <c>X-Inertia</c>.</summary>
    public bool IsInertia { get; }

    /// <summary>The client's asset version.</summary>
    public string? Version { get; }

    /// <summary>The component a partial reload targets.</summary>
    public string? PartialComponent { get; }

    /// <summary>Paths to include on a partial reload (null = no filter).</summary>
    public string[]? Only { get; }

    /// <summary>Paths to exclude on a partial reload (null = no filter).</summary>
    public string[]? Except { get; }

    /// <summary>Paths whose merge state is reset.</summary>
    public string[]? Reset { get; }

    /// <summary>The error bag name.</summary>
    public string? ErrorBag { get; }

    /// <summary>Once keys the client already holds.</summary>
    public string[]? ExceptOnceProps { get; }

    /// <summary>The infinite-scroll merge intent is <c>prepend</c>.</summary>
    public bool MergeIntentPrepend { get; }

    /// <summary>The request is a prefetch (<c>Purpose: prefetch</c>).</summary>
    public bool IsPrefetch { get; }

    private static string? NullIfEmpty(StringValues values)
    {
        string? value = values;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string[]? ParseList(StringValues values)
    {
        List<string>? list = null;
        foreach (var value in values)
        {
            var span = value.AsSpan();
            foreach (var range in span.Split(','))
            {
                var part = span[range].Trim();
                if (!part.IsEmpty)
                {
                    (list ??= []).Add(part.ToString());
                }
            }
        }

        return list?.ToArray();
    }
}
