using System.Text.Json.Nodes;

namespace Inertia.Net.Testing;

/// <summary>Fluent assertions over an Inertia page. The assertion scope is the page <c>props</c>, like Laravel's <c>AssertableInertia</c>.</summary>
public sealed class AssertableInertia : AssertableJsonBase<AssertableInertia>
{
    private readonly InertiaPage _page;

    /// <summary>Creates assertions for <paramref name="page"/>.</summary>
    public AssertableInertia(InertiaPage page) : base(page.Props, "") => _page = page;

    private static void Fail(string message) => throw new InertiaAssertionException(message);

    /// <summary>Asserts the HTTP response status code.</summary>
    public AssertableInertia Status(int expected)
    {
        if (_page.StatusCode != expected) Fail($"Unexpected Inertia response status: expected [{expected}] but was [{_page.StatusCode}].");
        return this;
    }

    /// <summary>Asserts the page component.</summary>
    public AssertableInertia Component(string expected)
    {
        if (_page.Component != expected) Fail($"Unexpected Inertia page component: expected [{expected}] but was [{_page.Component}].");
        return this;
    }

    /// <summary>Asserts the page URL.</summary>
    public AssertableInertia Url(string expected)
    {
        if (_page.Url != expected) Fail($"Unexpected Inertia page url: expected [{expected}] but was [{_page.Url}].");
        return this;
    }

    /// <summary>Asserts the asset version.</summary>
    public AssertableInertia Version(string expected)
    {
        if (_page.Version != expected) Fail($"Unexpected Inertia asset version: expected [{expected}] but was [{_page.Version}].");
        return this;
    }

    /// <summary>
    /// Asserts that validation errors contain at least one message. Without a bag name, checks both default fields and named
    /// bags; a bag name selects that exact bag, including any literal dots in its name.
    /// </summary>
    public AssertableInertia HasErrors(string? bag = null)
    {
        var errors = ErrorFields(bag);
        var found = errors.Any(error => HasMessages(error.Value)
            || bag is null && error.Value is JsonObject fields && fields.Any(field => HasMessages(field.Value)));
        if (!found) Fail(bag is null ? "No validation errors were found." : $"No validation errors were found in bag [{bag}].");
        return this;
    }

    /// <summary>
    /// Asserts that a field has a validation error and optionally an exact message, including any member of an all-errors
    /// array. Field names are literal client keys such as <c>items.0.email</c>. Pass <paramref name="bag"/> for a named bag.
    /// </summary>
    public AssertableInertia HasError(string field, string? message = null, string? bag = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        var errors = ErrorFields(bag);
        if (!errors.TryGetPropertyValue(field, out var value) || !HasMessages(value))
            Fail($"Validation error [{field}] was not found{(bag is null ? "" : $" in bag [{bag}]")}.");
        if (message is not null && !HasMessage(value, message))
            Fail($"Validation error [{field}] does not contain message [{message}]{(bag is null ? "" : $" in bag [{bag}]")}.");
        return this;
    }

    /// <summary>Asserts that the exact named validation error bag exists and contains at least one field message.</summary>
    public AssertableInertia HasErrorBag(string bag)
    {
        ArgumentNullException.ThrowIfNull(bag);
        var errors = ErrorFields(null);
        if (errors[bag] is not JsonObject fields || !fields.Any(field => HasMessages(field.Value)))
            Fail($"Validation error bag [{bag}] was not found or contains no errors.");
        return this;
    }

    private JsonObject ErrorFields(string? bag)
    {
        Has("errors");
        var errors = _page.Props["errors"] as JsonObject
            ?? throw new InertiaAssertionException("Validation errors are not an object.");
        if (bag is null) return errors;
        HasErrorBag(bag);
        return (JsonObject)errors[bag]!;
    }

    private static bool HasMessages(JsonNode? value) => value switch
    {
        JsonValue message => message.TryGetValue<string>(out _),
        JsonArray messages => messages.Any(message => message is JsonValue item && item.TryGetValue<string>(out _)),
        _ => false,
    };

    private static bool HasMessage(JsonNode? value, string expected) => value switch
    {
        JsonValue message => message.TryGetValue<string>(out var text) && text == expected,
        JsonArray messages => messages.Any(message => message is JsonValue item && item.TryGetValue<string>(out var text) && text == expected),
        _ => false,
    };

    /// <summary>Asserts that flash data contains the key (dot path).</summary>
    public AssertableInertia HasFlash(string key)
    {
        if (!Json.TryGet(_page.Flash, key, out _)) Fail($"Inertia Flash Data is missing key [{key}].");
        return this;
    }

    /// <summary>Asserts that flash data contains the key with a value deeply equal to <paramref name="expected"/>.</summary>
    public AssertableInertia HasFlash(string key, object? expected)
    {
        HasFlash(key);
        Json.TryGet(_page.Flash, key, out var actual);
        if (!JsonNode.DeepEquals(actual, Json.ToNode(expected)))
            Fail($"Inertia Flash Data [{key}] expected to be {Json.Show(Json.ToNode(expected))} but was {Json.Show(actual)}.");
        return this;
    }

    /// <summary>Asserts that flash data does not contain the key.</summary>
    public AssertableInertia MissingFlash(string key)
    {
        if (Json.TryGet(_page.Flash, key, out _)) Fail($"Inertia Flash Data has unexpected key [{key}].");
        return this;
    }

    /// <summary>Asserts that <paramref name="path"/> is listed as a deferred prop in any group.</summary>
    public AssertableInertia HasDeferred(string path)
    {
        if (!_page.DeferredProps.Values.Any(p => p.Contains(path))) Fail($"Deferred prop [{path}] was not found in any group.");
        return this;
    }

    /// <summary>Asserts that <paramref name="path"/> is listed as a deferred prop in <paramref name="group"/>.</summary>
    public AssertableInertia HasDeferred(string group, string path)
    {
        if (!_page.DeferredProps.TryGetValue(group, out var paths) || !paths.Contains(path))
            Fail($"Deferred prop [{path}] was not found in group [{group}].");
        return this;
    }

    /// <summary>Asserts that <paramref name="path"/> has infinite-scroll metadata.</summary>
    public AssertableInertia HasScroll(string path)
    {
        if (_page.ScrollProps[path] is not JsonObject) Fail($"Scroll prop [{path}] was not found.");
        return this;
    }

    /// <summary>Asserts the current and next page values in the infinite-scroll metadata.</summary>
    public AssertableInertia HasScroll(string path, object? currentPage, object? nextPage)
    {
        HasScroll(path);
        var metadata = _page.ScrollProps[path]!;
        if (!JsonNode.DeepEquals(metadata["currentPage"], Json.ToNode(currentPage)))
            Fail($"Scroll prop [{path}] current page expected to be {Json.Show(Json.ToNode(currentPage))} but was {Json.Show(metadata["currentPage"])}.");
        if (!JsonNode.DeepEquals(metadata["nextPage"], Json.ToNode(nextPage)))
            Fail($"Scroll prop [{path}] next page expected to be {Json.Show(Json.ToNode(nextPage))} but was {Json.Show(metadata["nextPage"])}.");
        return this;
    }

    /// <summary>Asserts that <paramref name="path"/> is listed as a live prop.</summary>
    public AssertableInertia HasLive(string path)
    {
        if (!_page.LiveProps.ContainsKey(path)) Fail($"Live prop [{path}] was not found.");
        return this;
    }

    /// <summary>
    /// Asserts that the live prop at <paramref name="path"/> listens to <paramref name="event"/> on <paramref name="channel"/>, given as
    /// <c>"{type}:{name}"</c> (the <c>LiveChannel.Key</c>, e.g. <c>private:orders.1</c>).
    /// </summary>
    public AssertableInertia HasLive(string path, string channel, string @event)
    {
        HasLive(path);
        var listeners = _page.LiveProps[path]?["listeners"] as JsonArray ?? [];
        var found = listeners.Any(l =>
            $"{(string?)l?["channel"]?["type"]}:{(string?)l?["channel"]?["name"]}" == channel
            && (l?["events"] as JsonArray ?? []).Any(e => (string?)e == @event));
        if (!found) Fail($"Live prop [{path}] does not listen to [{@event}] on channel [{channel}].");
        return this;
    }
}
