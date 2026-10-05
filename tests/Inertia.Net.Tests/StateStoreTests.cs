using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Tests;

/// <summary>The cookie state store and the persisted payload.</summary>
public class StateStoreTests
{
    private readonly Harness _harness = new(configureServices: s => s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));

    private IInertiaStateStore Store => _harness.Services.GetRequiredService<IInertiaStateStore>();

    [Fact]
    public void Cookie_store_is_the_default()
    {
        Assert.IsType<CookieInertiaStateStore>(Store);
        Assert.IsType<SessionInertiaStateStore>(new Harness(o => o.State.UseSession()).Services.GetRequiredService<IInertiaStateStore>());
    }

    [Fact]
    public async Task State_round_trips_through_the_cookie_into_the_next_page()
    {
        var first = _harness.Context();
        first.Inertia()
            .Flash("user", new User("Ann", "ann@example.com"))
            .Flash("count", 3)
            .WithErrors(new Dictionary<string, string[]> { ["email"] = ["Taken", "Invalid"] }, "login")
            .ClearHistory()
            .PreserveFragment();
        var cookie = await SaveAsync(first);

        var next = _harness.Context();
        next.Request.Headers.Cookie = cookie;
        Assert.True(InertiaStateSerializer.TryRestore(next.Inertia(), (await Store.LoadAsync(next))!));
        var page = Harness.ParsePage(await Harness.ExecuteAsync(next.AsInertia(), Inertia.Render("Page")));

        JsonAssert.Equal("""{"user":{"name":"Ann","email":"ann@example.com"},"count":3}""", page["flash"]);
        JsonAssert.Equal("""{"login":{"email":"Taken"}}""", page["props"]!["errors"]);
        Assert.True((bool)page["clearHistory"]!);
        Assert.True((bool)page["preserveFragment"]!);
    }

    [Fact]
    public async Task Cookie_is_http_only_lax_root_path_and_secure_on_https()
    {
        var context = _harness.Context();
        context.Inertia().Flash("a", 1);
        var header = await SaveAsync(context, rawHeader: true);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", header, StringComparison.OrdinalIgnoreCase);

        var https = _harness.Context();
        https.Request.Scheme = "https";
        https.Inertia().Flash("a", 1);
        Assert.Contains("secure", await SaveAsync(https, rawHeader: true), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("not+base64!")]
    [InlineData("")]
    public async Task Tampered_cookies_are_ignored(string value)
    {
        var context = _harness.Context();
        context.Request.Headers.Cookie = $".Inertia.State={value}";
        var loaded = await Store.LoadAsync(context);
        if (value.Length == 0)
        {
            Assert.Null(loaded);
            return;
        }

        Assert.NotNull(loaded);
        Assert.Empty(loaded); // "clear it": the middleware deletes the cookie
        Assert.False(InertiaStateSerializer.TryRestore(context.Inertia(), loaded));
    }

    [Fact]
    public async Task A_modified_cookie_fails_authentication()
    {
        var context = _harness.Context();
        context.Inertia().Flash("a", 1);
        var cookie = await SaveAsync(context);
        var value = cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..];
        var tampered = value[..10] + (value[10] == 'A' ? 'B' : 'A') + value[11..];

        var next = _harness.Context();
        next.Request.Headers.Cookie = $".Inertia.State={tampered}";
        Assert.Empty((await Store.LoadAsync(next))!);
    }

    [Fact]
    public async Task Large_cookies_log_a_warning()
    {
        var context = _harness.Context();
        context.Inertia().Flash("big", new string('x', 2000));
        await SaveAsync(context);
        Assert.DoesNotContain(_harness.Logs.Entries, e => e.Level == LogLevel.Warning);

        context = _harness.Context();
        context.Inertia().Flash("big", new string('x', 4000));
        await SaveAsync(context);
        Assert.Contains(_harness.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("4096", StringComparison.Ordinal));
    }

    [Fact]
    public void Restored_state_is_carried_on_with_new_state_when_persisted_again()
    {
        var feature = new InertiaFeature();
        Assert.True(InertiaStateSerializer.TryRestore(feature, """{"flash":{"a":1},"errors":{"default":{"f":["m"]}},"clearHistory":true}"""u8.ToArray()));
        Assert.True(feature.HasState);
        Assert.True(feature.HistoryCleared);

        feature.Flash("b", 2);
        Assert.Equal("""{"errors":{"default":{"f":["m"]}},"flash":{"a":1,"b":2},"clearHistory":true}""", Encoding.UTF8.GetString(InertiaStateSerializer.Serialize(feature, PageOptions())));
        Assert.Equal(["a", "b"], feature.FlashData!.Keys);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"errors\":{\"default\":{\"f\":\"not an array\"}}}")]
    [InlineData("{not json")]
    public void Malformed_state_restores_nothing(string json)
    {
        var feature = new InertiaFeature();
        Assert.False(InertiaStateSerializer.TryRestore(feature, Encoding.UTF8.GetBytes(json)));
        Assert.Null(feature.Errors);
        Assert.Null(feature.FlashData);
    }

    private System.Text.Json.JsonSerializerOptions PageOptions() => _harness.Services.GetRequiredService<InertiaPageWriter>().SerializerOptions;

    // Saves the context's pending state and returns the request Cookie header for the next request (or the raw Set-Cookie header).
    private async Task<string> SaveAsync(HttpContext context, bool rawHeader = false)
    {
        await Store.SaveAsync(context, InertiaStateSerializer.Serialize(context.Inertia(), PageOptions()));
        var setCookie = context.Response.Headers.SetCookie.ToString();
        return rawHeader ? setCookie : setCookie[..setCookie.IndexOf(';', StringComparison.Ordinal)];
    }
}
