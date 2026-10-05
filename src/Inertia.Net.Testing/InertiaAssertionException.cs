namespace Inertia.Net.Testing;

/// <summary>Thrown when an Inertia assertion fails or a response is not a valid Inertia page.</summary>
public sealed class InertiaAssertionException(string message) : Exception(message);
