using System.Text.Json;
using System.Text.Json.Nodes;

namespace Inertia.Net.Testing;

/// <summary>JSON helpers shared by the assertion and page types.</summary>
internal static class Json
{
    /// <summary>Resolves a dot path (object keys and array indexes) under <paramref name="root"/>.</summary>
    public static bool TryGet(JsonNode? root, string path, out JsonNode? value)
    {
        value = root;
        if (path.Length == 0) return true;
        foreach (var seg in path.Split('.'))
        {
            switch (value)
            {
                case JsonObject o when o.TryGetPropertyValue(seg, out var v): value = v; break;
                case JsonArray a when int.TryParse(seg, out var i) && i >= 0 && i < a.Count: value = a[i]; break;
                default: value = null; return false;
            }
        }
        return true;
    }

    public static JsonNode? ToNode(object? o) => o switch
    {
        null => null,
        JsonNode n => n,
        _ => JsonSerializer.SerializeToNode(o), // reflection serialization: fine for tests
    };

    public static string Show(JsonNode? n) => n?.ToJsonString() ?? "null";

    public static IEnumerable<string> Keys(JsonNode? n) => n switch
    {
        JsonObject o => o.Select(p => p.Key),
        JsonArray a => Enumerable.Range(0, a.Count).Select(i => i.ToString()),
        _ => [],
    };

    public static string TypeOf(JsonNode? n) => n switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        _ => n.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => n.ToJsonString().AsSpan().IndexOfAny('.', 'e', 'E') >= 0 ? "double" : "integer",
        },
    };
}

/// <summary>Fluent assertions over a JSON scope, with Laravel-style strict interaction tracking.</summary>
/// <typeparam name="TSelf">The concrete assertion type, so chained calls keep their static type.</typeparam>
public abstract class AssertableJsonBase<TSelf> where TSelf : AssertableJsonBase<TSelf>
{
    private readonly HashSet<string> _interacted = [];
    private readonly JsonNode? _node;
    private readonly string _path;

    internal AssertableJsonBase(JsonNode? node, string path) => (_node, _path) = (node, path);

    private TSelf Self => (TSelf)this;

    private string Dot(string key) => _path.Length == 0 ? key : key.Length == 0 ? _path : $"{_path}.{key}";

    private static void Fail(string message) => throw new InertiaAssertionException(message);

    private void Touch(string key) => _interacted.Add(key.Split('.')[0]);

    private JsonNode? Get(string key)
    {
        if (!Json.TryGet(_node, key, out var v)) Fail($"Property [{Dot(key)}] does not exist.");
        Touch(key);
        return v;
    }

    private void RunScope(string key, Action<AssertableJson> scope)
    {
        Json.TryGet(_node, key, out var v);
        if (v is not (JsonObject or JsonArray)) Fail($"Property [{Dot(key)}] is not scopeable.");
        var child = new AssertableJson(v, Dot(key));
        scope(child);
        child.Interacted();
    }

    /// <summary>Fails if any property of this scope was not asserted (called automatically at the end of a nested scope).</summary>
    public void Interacted()
    {
        var left = Json.Keys(_node).Except(_interacted).ToArray();
        if (left.Length > 0)
            Fail((_path.Length > 0 ? $"Unexpected properties were found in scope [{_path}]" : "Unexpected properties were found on the root level")
                + $": [{string.Join(", ", left)}].");
    }

    /// <summary>Disables the interaction check for this scope.</summary>
    public TSelf Etc()
    {
        _interacted.UnionWith(Json.Keys(_node));
        return Self;
    }

    /// <summary>Asserts that the property exists (dot path, array indexes allowed).</summary>
    public TSelf Has(string key) { Get(key); return Self; }

    /// <summary>Asserts that the property exists and has <paramref name="count"/> children.</summary>
    public TSelf Has(string key, int count) { Get(key); return Count(key, count); }

    /// <summary>Asserts that the property exists, then asserts inside it. Unasserted children fail unless <c>Etc()</c> is called.</summary>
    public TSelf Has(string key, Action<AssertableJson> scope) { Get(key); RunScope(key, scope); return Self; }

    /// <summary>Asserts that the property has <paramref name="count"/> children and asserts inside the first one.</summary>
    public TSelf Has(string key, int count, Action<AssertableJson> firstItemScope)
    {
        Has(key, count);
        var first = Json.Keys(Get(key)).FirstOrDefault()
            ?? throw new InertiaAssertionException($"Cannot scope directly onto the first element of property [{Dot(key)}] because it is empty.");
        RunScope($"{key}.{first}", firstItemScope);
        return Self;
    }

    /// <summary>Asserts that every property exists.</summary>
    public TSelf HasAll(params string[] keys) { foreach (var k in keys) Has(k); return Self; }

    /// <summary>Asserts that at least one of the properties exists.</summary>
    public TSelf HasAny(params string[] keys)
    {
        var found = keys.Where(k => Json.TryGet(_node, k, out _)).ToArray();
        if (found.Length == 0) Fail($"None of properties [{string.Join(", ", keys.Select(Dot))}] exist.");
        foreach (var k in found) Touch(k);
        return Self;
    }

    /// <summary>Asserts that the property does not exist.</summary>
    public TSelf Missing(string key)
    {
        if (Json.TryGet(_node, key, out _)) Fail($"Property [{Dot(key)}] was found while it was expected to be missing.");
        return Self;
    }

    /// <summary>Asserts that none of the properties exist.</summary>
    public TSelf MissingAll(params string[] keys) { foreach (var k in keys) Missing(k); return Self; }

    /// <summary>Asserts that the property equals <paramref name="expected"/> (deep JSON equality; objects are serialized with reflection).</summary>
    public TSelf Where(string key, object? expected)
    {
        var actual = Get(key);
        if (!JsonNode.DeepEquals(actual, Json.ToNode(expected)))
            Fail($"Property [{Dot(key)}] expected to be {Json.Show(Json.ToNode(expected))} but was {Json.Show(actual)}.");
        return Self;
    }

    /// <summary>Asserts that the predicate accepts the property's value.</summary>
    public TSelf Where(string key, Func<JsonNode?, bool> predicate)
    {
        if (predicate is null) return WhereNull(key); // Where(key, null) binds here, not to the object overload
        if (!predicate(Get(key))) Fail($"Property [{Dot(key)}] was marked as invalid using a closure.");
        return Self;
    }

    /// <summary>Asserts that the property does not equal <paramref name="unexpected"/>.</summary>
    public TSelf WhereNot(string key, object? unexpected)
    {
        var actual = Get(key);
        if (JsonNode.DeepEquals(actual, Json.ToNode(unexpected)))
            Fail($"Property [{Dot(key)}] contains a value that should be missing: {Json.Show(actual)}.");
        return Self;
    }

    /// <summary>Asserts that the property exists and is JSON null.</summary>
    public TSelf WhereNull(string key)
    {
        if (Get(key) is { } v) Fail($"Property [{Dot(key)}] should be null but was {Json.Show(v)}.");
        return Self;
    }

    /// <summary>Asserts that the property exists and is not JSON null.</summary>
    public TSelf WhereNotNull(string key)
    {
        if (Get(key) is null) Fail($"Property [{Dot(key)}] should not be null.");
        return Self;
    }

    /// <summary>
    /// Asserts the property's type: one of <c>string, integer, double, number, boolean, array, object, null</c>,
    /// or several separated by <c>|</c>. <c>number</c> matches integers and doubles.
    /// </summary>
    public TSelf WhereType(string key, string types)
    {
        var actual = Json.TypeOf(Get(key));
        var allowed = types.Split('|', StringSplitOptions.TrimEntries);
        if (!allowed.Contains(actual) && !(allowed.Contains("number") && actual is "integer" or "double"))
            Fail($"Property [{Dot(key)}] is not of expected type [{types}]; got [{actual}].");
        return Self;
    }

    /// <summary>Asserts several property types at once (key to <c>"a|b"</c> type list).</summary>
    public TSelf WhereAllType(IReadOnlyDictionary<string, string> types)
    {
        foreach (var (k, t) in types) WhereType(k, t);
        return Self;
    }

    /// <summary>Asserts that the array property contains every expected value (deep equality).</summary>
    public TSelf WhereContains(string key, params object?[] expected)
    {
        if (Get(key) is not JsonArray arr) { Fail($"Property [{Dot(key)}] is not an array."); return Self; }
        var missing = expected.Select(Json.ToNode).Where(e => !arr.Any(a => JsonNode.DeepEquals(a, e))).Select(Json.Show).ToArray();
        if (missing.Length > 0) Fail($"Property [{Dot(key)}] does not contain [{string.Join(", ", missing)}].");
        return Self;
    }

    private int Size(string key) => Get(key) switch
    {
        JsonArray a => a.Count,
        JsonObject o => o.Count,
        _ => throw new InertiaAssertionException($"Property [{Dot(key)}] is not countable."),
    };

    /// <summary>Asserts that the array or object property has exactly <paramref name="expected"/> children.</summary>
    public TSelf Count(string key, int expected)
    {
        var n = Size(key);
        if (n != expected) Fail($"Property [{Dot(key)}] does not have the expected size: expected {expected} but was {n}.");
        return Self;
    }

    /// <summary>Asserts that the property's size is within <paramref name="min"/> and <paramref name="max"/> (inclusive).</summary>
    public TSelf CountBetween(string key, int min, int max)
    {
        var n = Size(key);
        if (n < min || n > max) Fail($"Property [{Dot(key)}] size is not between {min} and {max}: it was {n}.");
        return Self;
    }

    private string Where_() => _path.Length == 0 ? "the root level" : $"property [{_path}]";

    /// <summary>Asserts inside every child of this scope.</summary>
    public TSelf Each(Action<AssertableJson> scope)
    {
        var keys = Json.Keys(_node).ToArray();
        if (keys.Length == 0) Fail($"Cannot scope directly onto each element of {Where_()} because it is empty.");
        foreach (var k in keys) { Touch(k); RunScope(k, scope); }
        return Self;
    }

    /// <summary>Asserts inside the first child of this scope.</summary>
    public TSelf First(Action<AssertableJson> scope)
    {
        var first = Json.Keys(_node).FirstOrDefault();
        if (first is null) Fail($"Cannot scope directly onto the first element of {Where_()} because it is empty.");
        Touch(first!);
        RunScope(first!, scope);
        return Self;
    }

    /// <summary>Returns this scope as indented JSON (for debugging).</summary>
    public string Dump() => _node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
}

/// <summary>A nested assertion scope over a JSON object or array.</summary>
public sealed class AssertableJson : AssertableJsonBase<AssertableJson>
{
    internal AssertableJson(JsonNode? node, string path) : base(node, path) { }
}
