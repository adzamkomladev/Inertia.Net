namespace Inertia.Net.Testing;

/// <summary><see cref="HttpClient"/> and <see cref="HttpResponseMessage"/> entry points for Inertia testing.</summary>
public static class InertiaClientExtensions
{
    /// <summary>Sends an Inertia GET (<c>X-Inertia</c>, <c>X-Requested-With</c> and, when given, <c>X-Inertia-Version</c>).</summary>
    public static Task<HttpResponseMessage> InertiaGetAsync(this HttpClient client, string url, string? version = null,
        Action<HttpRequestMessage>? configure = null, CancellationToken cancellationToken = default) =>
        client.InertiaRequestAsync(HttpMethod.Get, url, null, version, configure, cancellationToken);

    /// <summary>Sends an Inertia request with any method and optional content.</summary>
    public static Task<HttpResponseMessage> InertiaRequestAsync(this HttpClient client, HttpMethod method, string url,
        HttpContent? content = null, string? version = null, Action<HttpRequestMessage>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Inertia", "true");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        if (version is not null) request.Headers.TryAddWithoutValidation("X-Inertia-Version", version);
        configure?.Invoke(request);
        return client.SendAsync(request, cancellationToken);
    }

    /// <summary>Parses the response as an Inertia page (JSON or initial HTML).</summary>
    public static Task<InertiaPage> GetInertiaPageAsync(this HttpResponseMessage response, CancellationToken cancellationToken = default) =>
        InertiaPage.ParseAsync(response, cancellationToken);

    /// <summary>Parses the response as an Inertia page, runs the assertions, and returns the page.</summary>
    public static async Task<InertiaPage> AssertInertiaAsync(this HttpResponseMessage response, Action<AssertableInertia>? assert = null,
        CancellationToken cancellationToken = default)
    {
        var page = await InertiaPage.ParseAsync(response, cancellationToken);
        assert?.Invoke(new AssertableInertia(page));
        return page;
    }
}
