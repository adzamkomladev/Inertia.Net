using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Tests;

/// <summary>Builds a service provider with Inertia registered and runs results against a <see cref="DefaultHttpContext"/>.</summary>
internal sealed class Harness
{
    public Harness(Action<InertiaOptions>? configure = null, TimeProvider? timeProvider = null, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services.AddLogging(b => b.AddProvider(Logs));
        // App-style JSON setup: a source-generated context first, reflection as the fallback.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, TestJsonContext.Default));
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        services.AddInertia(configure);
        Services = services.BuildServiceProvider();
    }

    public IServiceProvider Services { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public DefaultHttpContext Context(string url = "/", string method = "GET")
    {
        var context = new DefaultHttpContext { RequestServices = Services };
        context.Request.Method = method;
        var query = url.IndexOf('?', StringComparison.Ordinal);
        context.Request.Path = query < 0 ? url : url[..query];
        if (query >= 0)
        {
            context.Request.QueryString = new QueryString(url[query..]);
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    public static async Task<string> ExecuteAsync(HttpContext context, IResult result)
    {
        await result.ExecuteAsync(context);
        return Body(context);
    }

    public static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    /// <summary>Renders <c>TestComponent</c> and returns the page object (from JSON or from the HTML script tag).</summary>
    public async Task<JsonObject> PageAsync(object? props, Action<HttpContext>? arrange = null, string component = "TestComponent", string url = "/")
    {
        var context = Context(url);
        arrange?.Invoke(context);
        var body = await ExecuteAsync(context, Inertia.Render(component, props));
        return ParsePage(body);
    }

    public static JsonObject ParsePage(string body) => JsonNode.Parse(body.StartsWith('{') ? body : ExtractPageJson(body))!.AsObject();

    public static string ExtractPageJson(string html)
    {
        const string open = "type=\"application/json\">";
        var start = html.IndexOf(open, StringComparison.Ordinal) + open.Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        return html[start..end];
    }
}

internal static class RequestExtensions
{
    public static HttpContext WithHeader(this HttpContext context, string name, string value)
    {
        context.Request.Headers[name] = value;
        return context;
    }

    public static HttpContext AsInertia(this HttpContext context) => context.WithHeader(InertiaHeaders.Inertia, "true");

    public static HttpContext AsPartial(this HttpContext context, string? only = null, string? except = null, string component = "TestComponent")
    {
        context.AsInertia().WithHeader(InertiaHeaders.PartialComponent, component);
        if (only is not null)
        {
            context.WithHeader(InertiaHeaders.PartialData, only);
        }

        if (except is not null)
        {
            context.WithHeader(InertiaHeaders.PartialExcept, except);
        }

        return context;
    }
}

internal static class JsonAssert
{
    public static void Equal(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"Expected: {JsonNode.Parse(expected)?.ToJsonString()}\nActual:   {actual?.ToJsonString()}");

    public static void Missing(JsonNode? node, string key) => Assert.False(node!.AsObject().ContainsKey(key), $"'{key}' should be missing in {node.ToJsonString()}");
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}

public sealed record User(string Name, string? Email = null);

public sealed record Sidebar(string Label, InertiaProp Notifications);

public sealed record DashboardPage(string PageTitle, InertiaProp Stats, Sidebar Sidebar);

public sealed record PagedUsers(List<User> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}

public sealed record Row(string Name, InertiaProp? Extra);

public sealed record IgnoredMembers(
    string Name,
    [property: JsonIgnore] string Secret,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Maybe,
    [property: JsonPropertyName("custom_name")] string Renamed,
    InertiaProp Lazy);

public sealed record BigNumbers(long Long, ulong ULong, Int128 Int128, UInt128 UInt128, BigInteger Big, long? Nullable, List<long> List);

[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(List<User>))]
[JsonSerializable(typeof(DashboardPage))]
[JsonSerializable(typeof(PagedUsers))]
[JsonSerializable(typeof(List<Row>))]
[JsonSerializable(typeof(BigNumbers))]
[JsonSerializable(typeof(IgnoredMembers))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
