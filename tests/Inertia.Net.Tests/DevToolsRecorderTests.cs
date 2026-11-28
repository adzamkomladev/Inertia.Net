using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inertia.Net.Tests;

public sealed class DevToolsRecorderTests
{
    [Theory]
    [InlineData("always", "", false)]
    [InlineData("optional", "", false)]
    [InlineData("merge", "value", true)]
    [InlineData("merge", " other, value ", true)]
    [InlineData("merge", "", false)]
    public async Task Recorded_prop_kinds_and_reset_flags_match_the_definitions(string kind, string reset, bool isReset)
    {
        var harness = Recording();
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var context = harness.Context().AsInertia();
        context.Request.Headers[InertiaHeaders.Reset] = reset;
        var capture = recorder.Begin(context)!;
        var prop = kind switch
        {
            "always" => Inertia.Always(1),
            "optional" => Inertia.Optional(() => 1),
            _ => Inertia.Merge(new[] { 1 }),
        };
        await Harness.ExecuteAsync(context, Inertia.Render("Page", new InertiaProps { ["value"] = prop }));
        await recorder.CompleteAsync(context, capture, false);
        var entry = await ReadEntryAsync(harness, capture.Id);
        Assert.Equal(kind, (string?)entry["props"]!["value"]!["inertiaType"]);
        if (kind == "merge") Assert.Equal(isReset, (bool?)entry["props"]!["value"]!["reset"]);
        Assert.Equal("always", (string?)entry["props"]!["errors"]!["inertiaType"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Each_loader_records_its_duration(bool parallel)
    {
        var harness = Recording();
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var context = harness.Context().AsInertia();
        var capture = recorder.Begin(context)!;
        var slow = Inertia.Prop(async ct => { await Task.Delay(40, ct); return 1; });
        if (parallel) slow.Parallel();
        await Harness.ExecuteAsync(context, Inertia.Render("Page", new InertiaProps { ["slow"] = slow, ["plain"] = 2 }));
        await recorder.CompleteAsync(context, capture, false);
        var entry = await ReadEntryAsync(harness, capture.Id);
        Assert.InRange((double)entry["props"]!["slow"]!["durationMs"]!, 30, 10_000);
        Assert.Null(entry["props"]!["plain"]!["durationMs"]); // a value has no loader
    }

    [Fact]
    public async Task Each_loader_gets_a_child_activity_only_while_a_listener_is_attached()
    {
        var stopped = new List<System.Diagnostics.Activity>();
        // The listener sees every test's activities; this test keeps those under a root of its own.
        using var root = new System.Diagnostics.Activity("test").Start();
        using (var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == InertiaDiagnostics.ActivitySourceName,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.TraceId == root.TraceId) lock (stopped) stopped.Add(activity); },
        })
        {
            System.Diagnostics.ActivitySource.AddActivityListener(listener);
            await new Harness().PageAsync(new InertiaProps
            {
                ["a"] = Inertia.Prop(async ct => { await Task.Yield(); return 1; }),
                ["b"] = Inertia.Prop(async ct => { await Task.Yield(); return 2; }).Parallel(),
            });
        }

        System.Diagnostics.Activity[] props;
        lock (stopped) props = [.. stopped.Where(a => a.OperationName == "inertia.prop" && (string?)a.GetTagItem("inertia.prop") is "a" or "b")];
        Assert.Equal(["a", "b"], props.Select(a => (string?)a.GetTagItem("inertia.prop")).Order());
        var render = Assert.Single(stopped, a => a.OperationName == "inertia.render");
        Assert.All(props, a => Assert.Equal(render.SpanId, a.ParentSpanId));
    }

    [Theory]
    [InlineData("Production", true, false, true)]
    [InlineData("Staging", true, false, true)]
    [InlineData("Development", true, false, false)]
    [InlineData("Production", false, false, false)]
    [InlineData("Production", null, false, false)]
    [InlineData("Production", true, true, false)]
    public void Recording_without_a_reader_warns_only_when_enabled_outside_development(string environment, bool? enabled, bool authorize, bool warns)
    {
        var harness = new Harness(environment: environment, configureServices: services => services.AddInertiaDevTools(options =>
        {
            options.Enabled = enabled;
            if (authorize) options.Authorize = _ => true;
        }));
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        Assert.Equal(enabled ?? environment == "Development", recorder.Enabled);
        Assert.Equal(warns ? 1 : 0, harness.Logs.Entries.Count(entry => entry.Level == LogLevel.Warning));
        if (warns)
        {
            Assert.Contains("InertiaDevToolsOptions.Authorize", Assert.Single(harness.Logs.Entries).Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("127.0.0.1", "localhost", StatusCodes.Status200OK)]
    [InlineData("::1", "[::1]:5000", StatusCodes.Status200OK)]
    [InlineData(null, "app.localhost", StatusCodes.Status200OK)]
    [InlineData("10.0.0.5", "localhost", StatusCodes.Status404NotFound)]
    [InlineData("127.0.0.1", "attacker.example", StatusCodes.Status404NotFound)]
    public async Task Without_a_reader_development_serves_entries_only_to_this_machine(string? remote, string host, int status)
    {
        var harness = new Harness(environment: "Development", configureServices: services => services.AddInertiaDevTools());
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var context = new DefaultHttpContext { RequestServices = harness.Services };
        context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);
        context.Request.Host = HostString.FromUriComponent(host);
        context.Response.Body = new MemoryStream();

        await recorder.ListEntriesAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.7")]
    [InlineData("Forwarded", "for=203.0.113.7")]
    [InlineData("X-Forwarded-Host", "app.example.com")]
    [InlineData("X-Real-IP", "203.0.113.7")]
    [InlineData("Via", "1.1 proxy")]
    public async Task Without_a_reader_development_refuses_a_request_forwarded_by_a_local_proxy(string header, string value)
    {
        var harness = new Harness(environment: "Development", configureServices: services => services.AddInertiaDevTools());
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var context = new DefaultHttpContext { RequestServices = harness.Services };
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = HostString.FromUriComponent("127.0.0.1:5000");
        context.Request.Headers[header] = value;
        context.Response.Body = new MemoryStream();

        await recorder.ListEntriesAsync(context);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private static Harness Recording(Action<InertiaDevToolsOptions>? configure = null) =>
        new(environment: "Development", configureServices: services => services.AddInertiaDevTools(configure));

    // Records an Inertia visit to a page whose one prop is `size` characters; returns the response body and the stored entry.
    private static async Task<(string Body, JsonObject Entry)> RecordAsync(Harness harness, int size, string tab = "t")
    {
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var context = harness.Context().AsInertia();
        context.Request.Headers["X-Inertia-Devtools-Tab"] = tab;
        var capture = recorder.Begin(context)!;
        var body = await Harness.ExecuteAsync(context, Inertia.Render("Report", new InertiaProps { ["text"] = new string('x', size) }));
        await recorder.CompleteAsync(context, capture, failed: false);
        return (body, await ReadEntryAsync(harness, capture.Id));
    }

    private static async Task<JsonObject> ReadEntryAsync(Harness harness, string id)
    {
        var read = new DefaultHttpContext { RequestServices = harness.Services };
        read.Connection.RemoteIpAddress = IPAddress.Loopback;
        read.Request.Host = new HostString("localhost");
        read.Request.RouteValues["id"] = id;
        read.Response.Body = new MemoryStream();
        await harness.Services.GetRequiredService<DevToolsRecorder>().GetEntryAsync(read);
        return (JsonObject)JsonNode.Parse(Harness.Body(read))!;
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task A_page_over_the_response_limit_is_recorded_without_its_body(int over, bool kept)
    {
        var (_, probe) = await RecordAsync(Recording(), 10_000);
        var size = (int)probe["__meta"]!["responseBytes"]!;
        var harness = Recording(o => o.MaxResponseBytes = size - over);

        var (body, entry) = await RecordAsync(harness, 10_000);

        Assert.Equal(size, System.Text.Encoding.UTF8.GetByteCount(body)); // the browser still gets the whole page
        var meta = entry["__meta"]!;
        Assert.Equal("Report", (string?)meta["component"]);
        Assert.Equal("navigate", (string?)meta["requestType"]);
        Assert.Equal(200, (int)meta["status"]!);
        Assert.Equal(size, (int)meta["responseBytes"]!);
        var response = entry["http"]!["responseBody"]!;
        if (kept)
        {
            Assert.Equal("present", (string?)response["status"]);
            Assert.Equal(10_000, ((string?)response["value"]!["props"]!["text"])!.Length);
        }
        else
        {
            Assert.Equal("omitted", (string?)response["status"]);
            Assert.Equal("too-large", (string?)response["reason"]);
            Assert.Null(entry["propValues"]?["text"]);
        }
    }

    [Fact]
    public async Task Retained_entries_stay_within_the_byte_budget_across_tabs()
    {
        var (_, probe) = await RecordAsync(Recording(), 50_000);
        var entrySize = probe.ToJsonString().Length;
        var harness = Recording(o => o.MaxTotalBytes = entrySize * 3L + entrySize / 2);
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => RecordOnlyAsync(harness, 50_000, "tab" + (i % 3)), TestContext.Current.CancellationToken)));

        Assert.InRange(recorder.RetainedBytes, 1, entrySize * 3L + entrySize / 2);
        Assert.Equal(3, recorder.Count);
    }

    [Fact]
    public async Task An_entry_larger_than_the_whole_budget_is_not_kept()
    {
        var harness = Recording(o => o.MaxTotalBytes = 1_000);
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var body = await RecordOnlyAsync(harness, 50_000, "t");
        Assert.Contains("\"text\"", body, StringComparison.Ordinal);
        Assert.Equal(0, recorder.Count);
        Assert.Equal(0, recorder.RetainedBytes);
    }

    private static async Task<string> RecordOnlyAsync(Harness harness, int size, string tab)
    {
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        var context = harness.Context().AsInertia();
        context.Request.Headers["X-Inertia-Devtools-Tab"] = tab;
        var capture = recorder.Begin(context)!;
        var body = await Harness.ExecuteAsync(context, Inertia.Render("Report", new InertiaProps { ["text"] = new string('x', size) }));
        await recorder.CompleteAsync(context, capture, failed: false);
        return body;
    }
}
