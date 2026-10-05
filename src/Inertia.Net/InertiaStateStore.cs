using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// Carries flash data, validation errors and history flags from a redirecting request to the next one.
/// The middleware loads the state at the start of a request and then either saves new state (the response is a redirect)
/// or clears it (consumed). Implementations only move opaque bytes.
/// </summary>
public interface IInertiaStateStore
{
    /// <summary>Returns the stored state, null when there is none, or an empty array when unreadable state must be cleared.</summary>
    ValueTask<byte[]?> LoadAsync(HttpContext httpContext);

    /// <summary>Stores <paramref name="state"/> for the next request. Called before the response headers are sent.</summary>
    ValueTask SaveAsync(HttpContext httpContext, byte[] state);

    /// <summary>Removes the stored state. Called before the response headers are sent.</summary>
    ValueTask ClearAsync(HttpContext httpContext);
}

/// <summary>
/// Default store: a Data Protection encrypted, HttpOnly, SameSite=Lax cookie. Needs no session and is Native AOT safe.
/// Tampered or undecryptable cookies are ignored and deleted.
/// </summary>
internal sealed partial class CookieInertiaStateStore : IInertiaStateStore
{
    internal const int SizeWarningThreshold = 4000;

    private readonly IDataProtector _protector;
    private readonly string _cookieName;
    private readonly ILogger _logger;

    public CookieInertiaStateStore(IDataProtectionProvider dataProtection, IOptions<InertiaOptions> options, ILogger<CookieInertiaStateStore> logger)
    {
        _protector = dataProtection.CreateProtector("Inertia.Net.State");
        _cookieName = options.Value.State.CookieName;
        _logger = logger;
    }

    public ValueTask<byte[]?> LoadAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(_cookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        try
        {
            return ValueTask.FromResult<byte[]?>(_protector.Unprotect(WebEncoders.Base64UrlDecode(value)));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            LogUnreadable(_logger, _cookieName, ex);
            return ValueTask.FromResult<byte[]?>([]);
        }
    }

    public ValueTask SaveAsync(HttpContext httpContext, byte[] state)
    {
        var value = WebEncoders.Base64UrlEncode(_protector.Protect(state));
        if (value.Length > SizeWarningThreshold)
        {
            LogTooLarge(_logger, _cookieName, value.Length);
        }

        httpContext.Response.Cookies.Append(_cookieName, value, CreateCookieOptions(httpContext));
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(_cookieName, CreateCookieOptions(httpContext));
        return ValueTask.CompletedTask;
    }

    private static CookieOptions CreateCookieOptions(HttpContext httpContext) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = httpContext.Request.IsHttps,
        Path = "/",
        IsEssential = true,
    };

    [LoggerMessage(1, LogLevel.Debug, "Ignoring the unreadable Inertia state cookie '{CookieName}'.")]
    private static partial void LogUnreadable(ILogger logger, string cookieName, Exception exception);

    [LoggerMessage(2, LogLevel.Warning, "The Inertia state cookie '{CookieName}' is {Length} bytes; browsers drop cookies over 4096 bytes. Flash less data or use the session store (options.State.UseSession()).")]
    private static partial void LogTooLarge(ILogger logger, string cookieName, int length);
}

/// <summary>Opt-in store backed by <see cref="ISession"/>. Needs <c>AddSession()</c> and <c>app.UseSession()</c> before <c>app.UseInertia()</c>.</summary>
internal sealed class SessionInertiaStateStore(IOptions<InertiaOptions> options) : IInertiaStateStore
{
    private readonly string _key = options.Value.State.CookieName;

    public async ValueTask<byte[]?> LoadAsync(HttpContext httpContext)
    {
        var session = GetSession(httpContext);
        await session.LoadAsync(httpContext.RequestAborted);
        return session.TryGetValue(_key, out var state) ? state : null;
    }

    public ValueTask SaveAsync(HttpContext httpContext, byte[] state)
    {
        GetSession(httpContext).Set(_key, state);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(HttpContext httpContext)
    {
        GetSession(httpContext).Remove(_key);
        return ValueTask.CompletedTask;
    }

    private static ISession GetSession(HttpContext httpContext) =>
        httpContext.Features.Get<ISessionFeature>()?.Session
        ?? throw new InvalidOperationException("The Inertia session state store needs services.AddSession() and app.UseSession() before app.UseInertia().");
}

/// <summary>
/// The persisted state: <c>{"errors":{bag:{field:[messages]}},"flash":{key:value},"clearHistory":true,"preserveFragment":true}</c>.
/// Written and read with <see cref="Utf8JsonWriter"/>/<see cref="JsonDocument"/> (no reflection); flash values are serialized with
/// the page serializer options at persist time and restored as <see cref="JsonElement"/>s, which the page writer emits verbatim.
/// </summary>
internal static class InertiaStateSerializer
{
    public static byte[] Serialize(InertiaFeature feature, JsonSerializerOptions serializerOptions)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (feature.Errors is { Count: > 0 } bags)
            {
                writer.WriteStartObject("errors");
                foreach (var (bag, errors) in bags)
                {
                    writer.WriteStartObject(bag);
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
                }

                writer.WriteEndObject();
            }

            if (feature.FlashData is { Count: > 0 } flash)
            {
                writer.WriteStartObject("flash");
                foreach (var (key, value) in flash)
                {
                    writer.WritePropertyName(key);
                    switch (value)
                    {
                        case null:
                            writer.WriteNullValue();
                            break;
                        case JsonElement element:
                            element.WriteTo(writer);
                            break;
                        default:
                            JsonSerializer.Serialize(writer, value, serializerOptions.GetTypeInfo(value.GetType()));
                            break;
                    }
                }

                writer.WriteEndObject();
            }

            if (feature.HistoryCleared)
            {
                writer.WriteBoolean("clearHistory", true);
            }

            if (feature.FragmentPreserved)
            {
                writer.WriteBoolean("preserveFragment", true);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Restores <paramref name="state"/> into <paramref name="feature"/>; returns false (restoring nothing) when it is malformed.</summary>
    public static bool TryRestore(InertiaFeature feature, byte[] state)
    {
        if (state.Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(state);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // Validate everything before touching the feature, so malformed state restores nothing.
            var errors = new List<(string Bag, string Field, string[] Messages)>();
            if (root.TryGetProperty("errors", out var bags))
            {
                foreach (var bag in bags.EnumerateObject())
                {
                    foreach (var field in bag.Value.EnumerateObject())
                    {
                        var messages = new string[field.Value.GetArrayLength()];
                        var i = 0;
                        foreach (var message in field.Value.EnumerateArray())
                        {
                            messages[i++] = message.GetString() ?? "";
                        }

                        errors.Add((bag.Name, field.Name, messages));
                    }
                }
            }

            var flash = new List<(string Key, JsonElement Value)>();
            if (root.TryGetProperty("flash", out var flashElement))
            {
                foreach (var item in flashElement.EnumerateObject())
                {
                    flash.Add((item.Name, item.Value.Clone()));
                }
            }

            foreach (var (bag, field, messages) in errors)
            {
                feature.RestoreError(bag, field, messages);
            }

            foreach (var (key, value) in flash)
            {
                feature.RestoreFlash(key, value);
            }

            feature.RestoreFlags(
                root.TryGetProperty("clearHistory", out var clear) && clear.ValueKind == JsonValueKind.True,
                root.TryGetProperty("preserveFragment", out var preserve) && preserve.ValueKind == JsonValueKind.True);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
