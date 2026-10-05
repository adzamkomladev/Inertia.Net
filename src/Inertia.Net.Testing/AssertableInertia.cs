using System.Text.Json.Nodes;

namespace Inertia.Net.Testing;

/// <summary>Fluent assertions over an Inertia page. The assertion scope is the page <c>props</c>, like Laravel's <c>AssertableInertia</c>.</summary>
public sealed class AssertableInertia : AssertableJsonBase<AssertableInertia>
{
    private readonly InertiaPage _page;

    /// <summary>Creates assertions for <paramref name="page"/>.</summary>
    public AssertableInertia(InertiaPage page) : base(page.Props, "") => _page = page;

    private static void Fail(string message) => throw new InertiaAssertionException(message);

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
}
