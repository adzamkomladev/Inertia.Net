using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Inertia.Net.Mvc;

/// <summary>
/// An <see cref="IInertiaStateStore"/> backed by MVC TempData, so the state travels with whichever TempData provider the app uses
/// (the default cookie provider, or <c>AddSessionStateTempDataProvider()</c> with <c>AddSession()</c>/<c>UseSession()</c>).
/// TempData is saved explicitly: the store also runs for Minimal API endpoints, where no MVC filter saves it. Saving twice (here and
/// in MVC's <c>SaveTempDataFilter</c>) is harmless: it is the same per-request dictionary and the key is retained.
/// </summary>
internal sealed class TempDataInertiaStateStore(
    ITempDataDictionaryFactory factory,
    ITempDataProvider provider,
    IOptions<CookieTempDataProviderOptions> cookieOptions,
    IOptions<SessionOptions> sessionOptions) : IInertiaStateStore
{
    internal const string Key = "__Inertia.State";

    // The cookie the built-in providers need before there can be any TempData; null for a custom provider (always loaded).
    private readonly string? _requiredCookie = provider switch
    {
        CookieTempDataProvider => cookieOptions.Value.Cookie.Name,
        SessionStateTempDataProvider => sessionOptions.Value.Cookie.Name,
        _ => null,
    };

    public ValueTask<byte[]?> LoadAsync(HttpContext httpContext)
    {
        // No TempData cookie (or session cookie): nothing to load, so the provider (a decrypt, or a session store read) is skipped.
        if (_requiredCookie is not null && !httpContext.Request.Cookies.ContainsKey(_requiredCookie))
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        // Peek, not read: a read marks the key for removal, and MVC's SaveTempDataFilter (which runs before the middleware decides
        // to carry the state on) would then delete it. The middleware calls ClearAsync when the state is consumed.
        if (factory.GetTempData(httpContext).Peek(Key) is not string value)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        try
        {
            return ValueTask.FromResult<byte[]?>(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return ValueTask.FromResult<byte[]?>([]);
        }
    }

    public ValueTask SaveAsync(HttpContext httpContext, byte[] state)
    {
        var tempData = factory.GetTempData(httpContext);
        tempData[Key] = Convert.ToBase64String(state);
        tempData.Save();
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(HttpContext httpContext)
    {
        var tempData = factory.GetTempData(httpContext);
        tempData.Remove(Key);
        tempData.Save();
        return ValueTask.CompletedTask;
    }
}
