namespace Inertia.Net.Testing.Tests;

public class ClientTests
{
    [Fact]
    public async Task InertiaGetAsync_sends_inertia_headers()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.InertiaGetAsync("/users", "v7", req => req.Headers.Add("X-Custom", "1"));
        var h = Assert.Single(s.Requests).Headers;
        Assert.Equal("true", h["X-Inertia"]);
        Assert.Equal("v7", h["X-Inertia-Version"]);
        Assert.Equal("XMLHttpRequest", h["X-Requested-With"]);
        Assert.Equal("1", h["X-Custom"]);
        Assert.Equal("true", r.Headers.GetValues("X-Inertia").Single());
    }

    [Fact]
    public async Task InertiaGetAsync_omits_version_when_null()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.InertiaGetAsync("/users");
        Assert.DoesNotContain("X-Inertia-Version", s.Requests[0].Headers.Keys);
    }

    [Fact]
    public async Task InertiaRequestAsync_sends_method_and_content()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.InertiaRequestAsync(HttpMethod.Post, "/users", new StringContent("{}"), "v1");
        Assert.Equal("POST", s.Requests[0].Method);
        Assert.Equal("true", s.Requests[0].Headers["X-Inertia"]);
    }

    [Fact]
    public async Task ReloadOnly_sends_partial_headers_and_returns_filtered_page()
    {
        await using var s = await FakeServer.StartAsync();
        var page = await (await s.Client.InertiaGetAsync("/users?page=2", "v1")).GetInertiaPageAsync();
        var reloaded = await page.ReloadOnlyAsync(s.Client, "title", "users");

        var h = s.Requests[^1].Headers;
        Assert.Equal("/users?page=2", s.Requests[^1].PathAndQuery);
        Assert.Equal("true", h["X-Inertia"]);
        Assert.Equal("Users/Index", h["X-Inertia-Partial-Component"]);
        Assert.Equal("title,users", h["X-Inertia-Partial-Data"]);
        Assert.Equal("v1", h["X-Inertia-Version"]);
        Assert.Equal("XMLHttpRequest", h["X-Requested-With"]);
        Assert.NotNull(reloaded.Prop("title"));
        Assert.False(reloaded.HasProp("evil"));
        Assert.Empty(reloaded.DeferredProps);
    }

    [Fact]
    public async Task ReloadOnly_fails_when_requested_key_is_absent()
    {
        await using var s = await FakeServer.StartAsync();
        var page = await (await s.Client.InertiaGetAsync("/users", "v1")).GetInertiaPageAsync();
        var e = await Assert.ThrowsAsync<InertiaAssertionException>(() => page.ReloadOnlyAsync(s.Client, "nope"));
        Assert.Equal("Property [nope] does not exist.", e.Message);
    }

    [Fact]
    public async Task ReloadExcept_sends_except_header_and_asserts_missing()
    {
        await using var s = await FakeServer.StartAsync();
        var page = await (await s.Client.InertiaGetAsync("/users", "v1")).GetInertiaPageAsync();
        var reloaded = await page.ReloadExceptAsync(s.Client, "users", "evil");

        var h = s.Requests[^1].Headers;
        Assert.Equal("Users/Index", h["X-Inertia-Partial-Component"]);
        Assert.Equal("users,evil", h["X-Inertia-Partial-Except"]);
        Assert.DoesNotContain("X-Inertia-Partial-Data", h.Keys);
        Assert.True(reloaded.HasProp("title"));
        Assert.False(reloaded.HasProp("users"));
    }

    [Fact]
    public async Task ReloadExcept_fails_when_server_still_returns_the_key()
    {
        await using var s = await FakeServer.StartAsync();
        var page = await (await s.Client.InertiaGetAsync("/stubborn", "v1")).GetInertiaPageAsync();
        var e = await Assert.ThrowsAsync<InertiaAssertionException>(() => page.ReloadExceptAsync(s.Client, "users"));
        Assert.Equal("Property [users] was found while it was expected to be missing.", e.Message);
    }

    [Fact]
    public async Task LoadDeferredProps_loads_all_groups_by_default()
    {
        await using var s = await FakeServer.StartAsync();
        var page = await (await s.Client.InertiaGetAsync("/users", "v1")).GetInertiaPageAsync();
        Assert.False(page.HasProp("stats"));

        var loaded = await page.LoadDeferredPropsAsync(s.Client);
        var h = s.Requests[^1].Headers;
        Assert.Equal("plans,stats", string.Join(',', h["X-Inertia-Partial-Data"].Split(',').Order()));
        Assert.Equal("Users/Index", h["X-Inertia-Partial-Component"]);
        Assert.Equal("v1", h["X-Inertia-Version"]);
        Assert.Equal(2, (int)loaded.Prop("stats.total")!);
        Assert.Equal("pro", (string)loaded.Prop("plans.1")!);
    }

    [Fact]
    public async Task LoadDeferredProps_loads_only_requested_groups()
    {
        await using var s = await FakeServer.StartAsync();
        var page = await (await s.Client.InertiaGetAsync("/users", "v1")).GetInertiaPageAsync();
        var loaded = await page.LoadDeferredPropsAsync(s.Client, "sidebar");
        Assert.Equal("stats", s.Requests[^1].Headers["X-Inertia-Partial-Data"]);
        Assert.True(loaded.HasProp("stats"));
        Assert.False(loaded.HasProp("plans"));
    }

    [Fact]
    public async Task LoadDeferredProps_fails_without_matching_groups()
    {
        await using var s = await FakeServer.StartAsync();
        var page = await (await s.Client.InertiaGetAsync("/users", "v1")).GetInertiaPageAsync();
        var e = await Assert.ThrowsAsync<InertiaAssertionException>(() => page.LoadDeferredPropsAsync(s.Client, "nope"));
        Assert.Equal("No deferred props to load in groups [nope].", e.Message);
    }

    [Fact]
    public async Task Works_from_html_page()
    {
        await using var s = await FakeServer.StartAsync();
        using var r = await s.Client.GetAsync("/users");
        var page = await r.AssertInertiaAsync(p => p.Component("Users/Index").HasDeferred("stats"));
        var loaded = await page.LoadDeferredPropsAsync(s.Client, "sidebar");
        Assert.True(loaded.HasProp("stats"));
    }
}
