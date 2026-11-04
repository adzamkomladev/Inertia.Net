namespace Inertia.Net;

/// <summary>
/// Marks an endpoint configured with <c>WithInertiaPrecognition()</c>. Precognitive requests are validated without invoking
/// its handler. A rate-limit policy can combine this marker with <see cref="InertiaPrecognition.IsPrecognitive"/> to give
/// validation requests a separate bounded bucket; validation and middleware still consume resources.
/// </summary>
public sealed class InertiaPrecognitionMetadata;
