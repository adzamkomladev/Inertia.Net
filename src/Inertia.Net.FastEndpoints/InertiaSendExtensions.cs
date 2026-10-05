using FastEndpoints;
using Void = FastEndpoints.Void;

namespace Inertia.Net.FastEndpoints;

/// <summary>
/// Inertia responses for FastEndpoints' <c>Send</c> property: <c>await Send.InertiaAsync("Users/Index", props)</c>.
/// <para>
/// An endpoint that implements <c>ExecuteAsync</c> can instead return the result: FastEndpoints sends any
/// <see cref="Microsoft.AspNetCore.Http.IResult"/> it returns, so <c>Endpoint&lt;TRequest, InertiaResult&gt;</c> with
/// <c>return Render("Users/Index", props);</c> (or <c>Back()</c>, <c>Location(url)</c>) works.
/// </para>
/// </summary>
public static class InertiaSendExtensions
{
    /// <summary>Renders an Inertia page: JSON for Inertia requests, the root view otherwise. See <see cref="Inertia.Render"/>.</summary>
    /// <param name="send">The endpoint's <c>Send</c> property.</param>
    /// <param name="component">The client-side page component name.</param>
    /// <param name="props">An <see cref="InertiaProps"/>/dictionary, or a typed page-props object.</param>
    public static Task<Void> InertiaAsync<TRequest, TResponse>(this ResponseSender<TRequest, TResponse> send, string component, object? props = null)
        where TRequest : notnull =>
        send.ResultAsync(Inertia.Render(component, props));

    /// <summary>A full page visit to <paramref name="url"/>: 409 with <c>X-Inertia-Location</c> for Inertia requests, otherwise a 302.</summary>
    public static Task<Void> InertiaLocationAsync<TRequest, TResponse>(this ResponseSender<TRequest, TResponse> send, string url)
        where TRequest : notnull =>
        send.ResultAsync(Inertia.Location(url));

    /// <summary>
    /// Redirects back to the same-origin <c>Referer</c>, or to <paramref name="fallback"/> (<c>~/</c> is the app root). For errors or flash data send
    /// <c>Back().WithErrors(...).WithFlash(...)</c> with <c>Send.ResultAsync</c>.
    /// </summary>
    public static Task<Void> InertiaBackAsync<TRequest, TResponse>(this ResponseSender<TRequest, TResponse> send, string fallback = "~/")
        where TRequest : notnull =>
        send.ResultAsync(Inertia.Back(fallback));
}
