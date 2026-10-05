namespace Inertia.Net;

/// <summary>
/// Page props, or a nested literal object inside them. Keys keep insertion order and are emitted as-is.
/// Any <see cref="IDictionary{TKey, TValue}"/> or <see cref="IReadOnlyDictionary{TKey, TValue}"/> of <c>string</c> to <c>object?</c> works the same way.
/// </summary>
public sealed class InertiaProps : Dictionary<string, object?>
{
    /// <summary>Creates an empty props object.</summary>
    public InertiaProps()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>Creates an empty props object with the given capacity.</summary>
    public InertiaProps(int capacity)
        : base(capacity, StringComparer.Ordinal)
    {
    }
}
