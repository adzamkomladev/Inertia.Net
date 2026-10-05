using Microsoft.AspNetCore.Http;

namespace Inertia.Net;

/// <summary>Per-request Inertia state (shared props, flash, errors, history flags). Get it with <see cref="InertiaHttpContextExtensions.Inertia"/>.</summary>
public sealed class InertiaFeature
{
    internal Dictionary<string, object?>? SharedProps { get; private set; }

    internal Dictionary<string, object?>? FlashData { get; private set; }

    internal Dictionary<string, Dictionary<string, string[]>>? Errors { get; private set; }

    internal bool HistoryCleared { get; private set; }

    internal bool? HistoryEncrypted { get; private set; }

    internal bool FragmentPreserved { get; private set; }

    /// <summary>Shares a prop with the page rendered for this request. Page props with the same key win.</summary>
    public InertiaFeature Share(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        (SharedProps ??= new(StringComparer.Ordinal))[key] = value;
        return this;
    }

    /// <summary>Adds flash data, emitted as <c>page.flash</c>.</summary>
    public InertiaFeature Flash(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        (FlashData ??= new(StringComparer.Ordinal))[key] = value;
        return this;
    }

    /// <summary>Adds validation errors (field to messages) to an error bag.</summary>
    public InertiaFeature WithErrors(IDictionary<string, string[]> errors, string bag = "default")
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(bag);
        var target = GetBag(bag);
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
        var target = GetBag(bag);
        foreach (var (field, message) in errors)
        {
            target[field] = [message];
        }

        return this;
    }

    /// <summary>Asks the client to clear its history state (<c>clearHistory</c>).</summary>
    public InertiaFeature ClearHistory()
    {
        HistoryCleared = true;
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
        FragmentPreserved = true;
        return this;
    }

    private Dictionary<string, string[]> GetBag(string bag)
    {
        Errors ??= new(StringComparer.Ordinal);
        if (!Errors.TryGetValue(bag, out var target))
        {
            Errors[bag] = target = new(StringComparer.Ordinal);
        }

        return target;
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
