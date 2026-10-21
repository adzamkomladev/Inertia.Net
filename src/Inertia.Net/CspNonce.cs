using System.Buffers;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>Resolves and checks the request's CSP nonce (<see cref="InertiaOptions.CspNonce"/>).</summary>
internal static class CspNonce
{
    // CSP base64-value: 1*( ALPHA / DIGIT / "+" / "/" / "-" / "_" ) *2"=". Checked so the value can be written into an attribute as-is.
    private static readonly SearchValues<char> Base64Chars = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/-_");

    /// <summary>The nonce for <paramref name="httpContext"/>, or null when none is configured or the source returns none.</summary>
    /// <exception cref="InvalidOperationException">The source returned a value that is not a base64 or base64url string.</exception>
    public static string? Resolve(Func<HttpContext, string?>? source, HttpContext httpContext)
    {
        var nonce = source?.Invoke(httpContext);
        if (string.IsNullOrEmpty(nonce))
        {
            return null;
        }

        if (!IsValid(nonce))
        {
            throw new InvalidOperationException("InertiaOptions.CspNonce returned a value that is not a base64 or base64url string.");
        }

        return nonce;
    }

    internal static bool IsValid(ReadOnlySpan<char> nonce)
    {
        var body = nonce.TrimEnd('=');
        return !body.IsEmpty && nonce.Length - body.Length <= 2 && !body.ContainsAnyExcept(Base64Chars);
    }

    /// <summary>The <c> nonce="..."</c> attribute, with its leading space; empty without a nonce.</summary>
    public static string Attribute(string? nonce) => nonce is null ? "" : $" nonce=\"{nonce}\"";
}
