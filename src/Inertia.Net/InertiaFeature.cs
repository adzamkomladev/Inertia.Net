using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>
/// Per-request Inertia state (shared props, flash, errors, history flags). Get it with <see cref="InertiaHttpContextExtensions.Inertia"/>.
/// Flash, errors, <see cref="ClearHistory"/> and <see cref="PreserveFragment"/> set during a request that ends in a redirect are
/// carried by the <see cref="IInertiaStateStore"/> (see <c>app.UseInertia()</c>) through any further redirects to the next response
/// that is not a redirect, which renders (consumes) them.
/// </summary>
public sealed class InertiaFeature
{
    // Set during this request.
    private Dictionary<string, object?>? _flash;
    private Dictionary<string, Dictionary<string, string[]>>? _errors;
    private bool _clearHistory;
    private bool _preserveFragment;

    // Restored from the previous request: rendered on this one (consumed), or carried on by another redirect.
    private Dictionary<string, object?>? _restoredFlash;
    private Dictionary<string, Dictionary<string, string[]>>? _restoredErrors;
    private bool _restoredClearHistory;
    private bool _restoredPreserveFragment;

    internal Dictionary<string, object?>? SharedProps { get; private set; }

    /// <summary>Restored and pending flash data, as rendered on the page.</summary>
    internal Dictionary<string, object?>? FlashData => Merge(_restoredFlash, _flash);

    /// <summary>Restored and pending error bags, as rendered on the page.</summary>
    internal Dictionary<string, Dictionary<string, string[]>>? Errors => Merge(_restoredErrors, _errors);

    internal bool HistoryCleared => _clearHistory || _restoredClearHistory;

    internal bool? HistoryEncrypted { get; private set; }

    internal bool FragmentPreserved => _preserveFragment || _restoredPreserveFragment;

    /// <summary>The state store had data for this request; the middleware clears it unless it persists new state.</summary>
    internal bool StateLoaded { get; set; }

    /// <summary>The middleware has already saved or cleared the stored state for this request.</summary>
    internal bool StateFinished { get; set; }

    /// <summary>
    /// State to carry across a redirect: set during this request, or restored and not rendered yet (like Laravel, which re-flashes
    /// on every redirect and keeps the history flags until a page renders).
    /// </summary>
    internal bool HasState => FlashData is { Count: > 0 } || Errors is { Count: > 0 } || HistoryCleared || FragmentPreserved;

    /// <summary>Shares a prop with the page rendered for this request. Page props with the same key win.</summary>
    public InertiaFeature Share(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        (SharedProps ??= new(StringComparer.Ordinal))[key] = value;
        return this;
    }

    /// <summary>Adds flash data, emitted as <c>page.flash</c> on this page or, after a redirect, on the next one.</summary>
    public InertiaFeature Flash(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        (_flash ??= new(StringComparer.Ordinal))[key] = value;
        return this;
    }

    /// <summary>Adds validation errors (field to messages) to an error bag.</summary>
    public InertiaFeature WithErrors(IDictionary<string, string[]> errors, string bag = "default")
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(bag);
        var target = GetBag(_errors ??= new(StringComparer.Ordinal), bag);
        foreach (var (field, messages) in errors)
        {
            target[field] = messages;
        }

        return this;
    }

    /// <summary>Adds validation errors (field to message) to an error bag.</summary>
    public InertiaFeature WithErrors(IDictionary<string, string> errors, string bag = "default")
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(bag);
        var target = GetBag(_errors ??= new(StringComparer.Ordinal), bag);
        foreach (var (field, message) in errors)
        {
            target[field] = [message];
        }

        return this;
    }

    /// <summary>Asks the client to clear its history state (<c>clearHistory</c>).</summary>
    public InertiaFeature ClearHistory()
    {
        _clearHistory = true;
        return this;
    }

    /// <summary>Overrides <see cref="InertiaOptions.EncryptHistory"/> for this request.</summary>
    public InertiaFeature EncryptHistory(bool encrypt = true)
    {
        HistoryEncrypted = encrypt;
        return this;
    }

    /// <summary>Keeps the URL fragment across the next redirect (<c>preserveFragment</c>).</summary>
    public InertiaFeature PreserveFragment()
    {
        _preserveFragment = true;
        return this;
    }

    internal void RestoreFlash(string key, object? value) => (_restoredFlash ??= new(StringComparer.Ordinal))[key] = value;

    internal void RestoreError(string bag, string field, string[] messages) =>
        GetBag(_restoredErrors ??= new(StringComparer.Ordinal), bag)[field] = messages;

    internal void RestoreFlags(bool clearHistory, bool preserveFragment)
    {
        _restoredClearHistory = clearHistory;
        _restoredPreserveFragment = preserveFragment;
    }

    private static Dictionary<string, string[]> GetBag(Dictionary<string, Dictionary<string, string[]>> bags, string bag)
    {
        if (!bags.TryGetValue(bag, out var target))
        {
            bags[bag] = target = new(StringComparer.Ordinal);
        }

        return target;
    }

    // Pending entries override restored ones (per key; per field within a bag). Allocates only when both exist.
    private static Dictionary<string, object?>? Merge(Dictionary<string, object?>? restored, Dictionary<string, object?>? pending)
    {
        if (restored is null || pending is null)
        {
            return pending ?? restored;
        }

        var merged = new Dictionary<string, object?>(restored, StringComparer.Ordinal);
        foreach (var (key, value) in pending)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static Dictionary<string, Dictionary<string, string[]>>? Merge(Dictionary<string, Dictionary<string, string[]>>? restored, Dictionary<string, Dictionary<string, string[]>>? pending)
    {
        if (restored is null || pending is null)
        {
            return pending ?? restored;
        }

        var merged = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.Ordinal);
        foreach (var source in (ReadOnlySpan<Dictionary<string, Dictionary<string, string[]>>>)[restored, pending])
        {
            foreach (var (bag, errors) in source)
            {
                var target = GetBag(merged, bag);
                foreach (var (field, messages) in errors)
                {
                    target[field] = messages;
                }
            }
        }

        return merged;
    }
}

/// <summary><see cref="HttpContext"/> extensions for Inertia.</summary>
public static class InertiaHttpContextExtensions
{
    /// <summary>Gets (or creates) the per-request <see cref="InertiaFeature"/>.</summary>
    public static InertiaFeature Inertia(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var feature = httpContext.Features.Get<InertiaFeature>();
        if (feature is null)
        {
            feature = new InertiaFeature();
            httpContext.Features.Set(feature);
        }

        return feature;
    }
}
