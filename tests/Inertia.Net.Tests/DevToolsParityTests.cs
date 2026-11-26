using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

public sealed class DevToolsParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reader_authorization_receives_the_current_identity_even_in_development(bool allowed)
    {
        var harness = Recording(options => options.Authorize = http => http.User.HasClaim("devtools", "read"));
        var read = harness.Context();
        read.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            allowed ? new[] { new System.Security.Claims.Claim("devtools", "read") } : [], "test"));
        await harness.Services.GetRequiredService<DevToolsRecorder>().ListEntriesAsync(read);
        Assert.Equal(allowed ? 200 : 404, read.Response.StatusCode);
    }

    [Theory]
    [InlineData("/health", false)]
    [InlineData("/assets/app.js", false)]
    [InlineData("/health-check", true)]
    [InlineData("/dashboard", true)]
    public void Excluded_paths_do_not_start_a_capture(string path, bool recorded)
    {
        var harness = Recording(options => { options.ExcludePaths.Add("/health"); options.ExcludePaths.Add("/assets/*"); });
        var http = harness.Context(path);
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        Assert.Equal(recorded, recorder.Begin(http) is not null);
        Assert.Equal(recorded, http.Features.Get<DevToolsCapture>() is not null);
        Assert.Equal(0, recorder.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Non_inertia_requests_ignore_inertia_only_type_headers(bool rendered)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Inertia-Devtools-Deferred"] = "1";
        http.Request.Headers["X-Inertia-Devtools-Poll"] = "1";
        http.Request.Headers[InertiaHeaders.PartialComponent] = "Page";
        http.Request.Headers[InertiaHeaders.Purpose] = "prefetch";
        Assert.Equal(rendered ? "initial" : "http", DevToolsRecorder.RequestType(http.Request, rendered));
    }

    [Theory]
    [InlineData("Precognition")]
    [InlineData("X-Inertia-Devtools-Deferred")]
    [InlineData("X-Inertia-Devtools-Poll")]
    [InlineData("X-Inertia-Partial-Component")]
    public void Empty_type_headers_do_not_change_navigation_classification(string header)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[InertiaHeaders.Inertia] = "true";
        http.Request.Headers[header] = "";
        Assert.Equal("navigate", DevToolsRecorder.RequestType(http.Request, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Non_inertia_navigation_does_not_reuse_an_incoming_batch(bool inertia)
    {
        var harness = Recording();
        var http = harness.Context();
        if (inertia) http.AsInertia();
        http.Request.Headers["X-Inertia-Devtools-Parent"] = "parent";
        var entry = await RecordAsync(harness, http);
        Assert.Equal(inertia ? "parent" : null, (string?)entry["__meta"]!["batchId"]);
    }

    [Fact]
    public async Task Untagged_entries_use_global_limits_not_an_artificial_tab_limit()
    {
        var harness = Recording(options => { options.MaxEntriesPerTab = 1; options.MaxEntries = 10; });
        for (var i = 0; i < 3; i++) await RecordAsync(harness, harness.Context());
        Assert.Equal(3, (await ListAsync(harness)).Count);
        for (var i = 0; i < 3; i++)
        {
            var http = harness.Context();
            http.Request.Headers["X-Inertia-Devtools-Tab"] = "tab";
            await RecordAsync(harness, http);
        }
        Assert.Equal(4, (await ListAsync(harness)).Count);
    }

    [Fact]
    public async Task Invalid_utf8_is_omitted_instead_of_recording_replacement_text()
    {
        var harness = Recording();
        var http = harness.Context().AsInertia();
        http.Request.ContentType = "application/json";
        http.Request.ContentLength = 2;
        http.Request.Body = new MemoryStream([0xB1, 0x31]);
        http.Response.ContentType = "text/plain";
        var capture = Capture();
        capture.RecordResponse([0xB1, 0x31]);
        var entry = await RecordAsync(harness, http, capture);
        foreach (var name in new[] { "requestBody", "responseBody" })
        {
            Assert.Equal("omitted", (string?)entry["http"]![name]!["status"]);
            Assert.Equal("unserializable", (string?)entry["http"]![name]!["reason"]);
        }
    }

    [Fact]
    public async Task Configured_redaction_applies_inside_arrays_without_losing_siblings()
    {
        var harness = Recording(options => options.RedactedKeys.Add("ssn"));
        var http = harness.Context();
        http.Response.ContentType = "application/json";
        var capture = Capture();
        capture.RecordResponse("""{"users":[{"SSN":"private","name":"Ann"},{"nested":{"ssn":"hidden","ok":1}}]}"""u8);
        var entry = await RecordAsync(harness, http, capture);
        var value = entry["http"]!["responseBody"]!["value"]!;
        Assert.Equal("[REDACTED]", (string?)value["users"]![0]!["SSN"]);
        Assert.Equal("Ann", (string?)value["users"]![0]!["name"]);
        Assert.Equal("[REDACTED]", (string?)value["users"]![1]!["nested"]!["ssn"]);
        Assert.Equal(1, (int?)value["users"]![1]!["nested"]!["ok"]);
    }

    [Fact]
    public void Default_deferred_group_is_retained_on_delivery()
    {
        var capture = Capture(deferred: true);
        capture.RecordProp("stats", Defer(() => 1), new InertiaRequest(new HeaderDictionary()));
        Assert.Equal("default", capture.PropMetadata["stats"]["deferGroup"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("prepend", "prepend")]
    [InlineData("append", "append")]
    public void Scroll_classification_follows_the_request_merge_intent(string intent, string direction)
    {
        var capture = Capture();
        capture.RecordProp("feed", Scroll("value"), new InertiaRequest(new HeaderDictionary { [InertiaHeaders.InfiniteScrollMergeIntent] = intent }));
        Assert.Equal(direction, capture.PropMetadata["feed"]["mergeDirection"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(15, false)]
    [InlineData(16, false)]
    [InlineData(17, false)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    public async Task Seekable_request_bodies_obey_exact_limits_even_with_unknown_length(int size, bool unknown)
    {
        var harness = Recording(options => options.MaxBodyBytes = 16);
        var http = harness.Context().AsInertia();
        http.Request.ContentType = "application/json";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("\"" + new string('x', size - 2) + "\""));
        http.Request.Body.Position = 3;
        if (unknown) http.Request.Headers.TransferEncoding = "chunked";
        else http.Request.ContentLength = size;
        var entry = await RecordAsync(harness, http);
        Assert.Equal(3, http.Request.Body.Position);
        var body = entry["http"]!["requestBody"]!;
        Assert.Equal(size > 16 ? "omitted" : "present", (string?)body["status"]);
        if (size > 16) Assert.Equal("too-large", (string?)body["reason"]);
        else Assert.Equal(new string('x', size - 2), (string?)body["value"]);
    }

    [Fact]
    public async Task Retention_expires_at_the_exact_age_boundary()
    {
        var clock = new RecorderClock();
        var harness = new Harness(timeProvider: clock, environment: "Development", configureServices: services =>
            services.AddInertiaDevTools(options => options.MaxAge = TimeSpan.FromMinutes(1)));
        await RecordAsync(harness, harness.Context());
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Empty(await ListAsync(harness));
    }
    [Theory]
    [InlineData("plain", null, null, false)]
    [InlineData("always", "always", null, false)]
    [InlineData("optional", "optional", null, false)]
    [InlineData("once", "once", null, false)]
    [InlineData("merge", "merge", "append", false)]
    [InlineData("prepend", "merge", "prepend", false)]
    [InlineData("nested-append", "merge", "append", false)]
    [InlineData("nested-prepend", "merge", "prepend", false)]
    [InlineData("mixed", "merge", "append", false)]
    [InlineData("deep", "merge", "append", true)]
    [InlineData("deep-prepend", "merge", "prepend", true)]
    [InlineData("match", "merge", "append", true)]
    [InlineData("scroll", "scroll", "append", false)]
    public void Classifier_normalizes_each_wrapper_and_merge_direction(string kind, string? type, string? direction, bool deep)
    {
        InertiaProp? prop = kind switch
        {
            "plain" => null,
            "always" => Always("value"),
            "optional" => Optional(() => "value"),
            "once" => Once(() => "value"),
            "prepend" => Merge("value").Prepend(),
            "nested-append" => Merge("value").Append("rows"),
            "nested-prepend" => Merge("value").Prepend("rows"),
            "mixed" => Merge("value").Append("rows").Prepend("cols"),
            "deep" => Merge("value").DeepMerge(),
            "deep-prepend" => Merge("value").DeepMerge().Prepend(),
            "match" => Merge("value").MatchOn("id"),
            "scroll" => Scroll("value"),
            _ => Merge("value"),
        };
        var capture = Capture();
        capture.RecordProp("items", prop, new InertiaRequest(new HeaderDictionary()));
        var meta = capture.PropMetadata["items"];
        Assert.Equal(type, meta["inertiaType"]?.GetValue<string>());
        Assert.Equal(direction, meta["mergeDirection"]?.GetValue<string>());
        Assert.Equal(deep, meta["deepMerge"]!.GetValue<bool>());
        Assert.Equal(kind == "once", meta["once"]!.GetValue<bool>());
        Assert.False(meta["live"]!.GetValue<bool>());
        Assert.False(meta["reset"]!.GetValue<bool>());
        Assert.Null(meta["deferGroup"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Deferred_delivery_retains_group_once_and_live_independently(bool deferred, bool scroll)
    {
        var capture = Capture(deferred);
        var prop = scroll ? Scroll("value").Defer("sidebar").Once()
            : Defer(() => "value", "sidebar").Once().Live(LiveChannel.Public("stats"), "updated");
        capture.RecordProp("stats", prop, new InertiaRequest(new HeaderDictionary()));
        var meta = capture.PropMetadata["stats"];
        Assert.Equal(scroll ? "scroll" : deferred ? "defer" : null, meta["inertiaType"]?.GetValue<string>());
        Assert.Equal(deferred ? "sidebar" : null, meta["deferGroup"]?.GetValue<string>());
        Assert.True(meta["once"]!.GetValue<bool>());
        Assert.Equal(!scroll, meta["live"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("other", false)]
    [InlineData("first, items, last", true)]
    public void Reset_applies_to_plain_values_too(string reset, bool expected)
    {
        var capture = Capture();
        capture.RecordProp("items", null, new InertiaRequest(new HeaderDictionary { [InertiaHeaders.Reset] = reset }));
        Assert.Equal(expected, capture.PropMetadata["items"]["reset"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(0, "navigate")]
    [InlineData(1, "prefetch")]
    [InlineData(3, "partial")]
    [InlineData(7, "poll")]
    [InlineData(15, "deferred")]
    [InlineData(31, "precognition")]
    public void Combined_headers_use_the_documented_request_type_precedence(int flags, string type)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[InertiaHeaders.Inertia] = "true";
        if ((flags & 1) != 0) http.Request.Headers[InertiaHeaders.Purpose] = "prefetch";
        if ((flags & 2) != 0) http.Request.Headers[InertiaHeaders.PartialComponent] = "Page";
        if ((flags & 4) != 0) http.Request.Headers["X-Inertia-Devtools-Poll"] = "1";
        if ((flags & 8) != 0) http.Request.Headers["X-Inertia-Devtools-Deferred"] = "1";
        if ((flags & 16) != 0) http.Request.Headers[InertiaPrecognition.Header] = "true";
        Assert.Equal(type, DevToolsRecorder.RequestType(http.Request, true));
    }

    [Theory]
    [InlineData(200, "/ordinary", null, null)]
    [InlineData(302, "/ordinary", null, "/ordinary")]
    [InlineData(409, "/ordinary", "/inertia", "/inertia")]
    [InlineData(302, "/ordinary", "/inertia", "/inertia")]
    public async Task Redirect_metadata_prefers_inertia_and_ignores_success_location(int status, string location, string? inertia, string? expected)
    {
        var harness = Recording();
        var http = harness.Context();
        http.Response.StatusCode = status;
        http.Response.Headers.Location = location;
        if (inertia is not null) http.Response.Headers[InertiaHeaders.Location] = inertia;
        var entry = await RecordAsync(harness, http);
        Assert.Equal(expected, (string?)entry["__meta"]!["redirectLocation"]);
    }

    [Theory]
    [InlineData(15, "present")]
    [InlineData(16, "present")]
    [InlineData(17, "omitted")]
    public async Task Raw_response_capture_is_bounded_without_changing_the_response(int size, string status)
    {
        var harness = Recording(options => options.MaxResponseBytes = 16);
        var http = harness.Context();
        http.Response.ContentType = "text/plain";
        var capture = Capture(limit: 16);
        capture.RecordResponse(Encoding.UTF8.GetBytes(new string('x', size)));
        var entry = await RecordAsync(harness, http, capture);
        Assert.Equal(status, (string?)entry["http"]!["responseBody"]!["status"]);
        if (size > 16) Assert.Equal("too-large", (string?)entry["http"]!["responseBody"]!["reason"]);
        else Assert.Equal(new string('x', size), (string?)entry["http"]!["responseBody"]!["value"]);
    }

    [Theory]
    [InlineData("application/json", "{\"password\":\"secret\",\"name\":\"Ann\"}", "present", null)]
    [InlineData("text/plain", "{\"password\":\"secret\",\"name\":\"Ann\"}", "present", null)]
    [InlineData("application/json", "{\"password\":\"secret\",", "omitted", "unserializable")]
    [InlineData("text/plain", "{\"password\":\"secret\",", "omitted", "unserializable")]
    [InlineData("application/octet-stream", "secret", "omitted", "binary")]
    public async Task Raw_response_json_is_redacted_and_malformed_json_is_not_leaked(string contentType, string body, string status, string? reason)
    {
        var harness = Recording();
        var http = harness.Context();
        http.Response.ContentType = contentType;
        var capture = Capture();
        capture.RecordResponse(Encoding.UTF8.GetBytes(body));
        var entry = await RecordAsync(harness, http, capture);
        Assert.Equal(status, (string?)entry["http"]!["responseBody"]!["status"]);
        Assert.Equal(reason, (string?)entry["http"]!["responseBody"]!["reason"]);
        Assert.DoesNotContain("secret", entry.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uploads_are_summarized_without_reading_file_contents()
    {
        var harness = Recording();
        var http = harness.Context().AsInertia();
        http.Request.ContentType = "multipart/form-data; boundary=test";
        http.Request.ContentLength = 100;
        var files = new FormFileCollection
        {
            new FormFile(new ThrowingStream(), 0, 42, "avatar", "portrait.png") { Headers = new HeaderDictionary(), ContentType = "image/png" },
            new FormFile(new ThrowingStream(), 0, 10, "documents", "one.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" },
            new FormFile(new ThrowingStream(), 0, 20, "documents", "two.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" },
        };
        http.Features.Set<IFormFeature>(new FormFeature(new FormCollection(new Dictionary<string, StringValues>
        {
            ["name"] = "Ann",
            ["password"] = "secret",
        }, files)));
        var entry = await RecordAsync(harness, http);
        var body = entry["http"]!["requestBody"]!["value"]!;
        Assert.Equal("Ann", (string?)body["name"]);
        Assert.Equal("[REDACTED]", (string?)body["password"]);
        Assert.Equal("portrait.png", (string?)body["avatar"]!["name"]);
        Assert.Equal(42, (long?)body["avatar"]!["size"]);
        Assert.Equal("image/png", (string?)body["avatar"]!["mimeType"]);
        Assert.Equal(2, body["documents"]!.AsArray().Count);
        Assert.Equal("one.txt", (string?)body["documents"]![0]!["name"]);
        Assert.Equal("two.txt", (string?)body["documents"]![1]!["name"]);
    }

    [Fact]
    public async Task Request_capture_restores_the_original_position_and_records_multiple_headers()
    {
        var harness = Recording();
        var http = harness.Context().AsInertia();
        var bytes = "{\"name\":\"Ann\"}"u8.ToArray();
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentType = "application/json";
        http.Request.ContentLength = bytes.Length;
        http.Request.Body.Position = 5;
        http.Request.Headers["X-Test"] = new StringValues(["one", "two"]);
        var entry = await RecordAsync(harness, http);
        Assert.Equal(5, http.Request.Body.Position);
        Assert.Equal("Ann", (string?)entry["http"]!["requestBody"]!["value"]!["name"]);
        Assert.Equal("one,two", (string?)entry["http"]!["requestHeaders"]!["x-test"]);
    }

    [Fact]
    public async Task Recorder_failure_does_not_change_the_response()
    {
        var harness = Recording();
        var http = harness.Context().AsInertia();
        http.Request.ContentType = "application/json";
        http.Request.ContentLength = 1;
        http.Request.Body = new ThrowingStream();
        http.Response.StatusCode = 201;
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        await recorder.CompleteAsync(http, Capture(), false);

        Assert.Equal(201, http.Response.StatusCode);
        Assert.Equal(0, recorder.Count);
        Assert.Contains(harness.Logs.Entries, log => log.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Broadcasting_during_a_render_does_not_mutate_the_active_capture()
    {
        var harness = Recording();
        var http = harness.Context().AsInertia();
        var capture = Capture();
        http.Features.Set(capture);
        await Harness.ExecuteAsync(http, Render("Page", new InertiaProps
        {
            ["order"] = Prop(async () =>
            {
                var payload = await Inertia.BroadcastPropsAsync(http, new InertiaProps
                {
                    ["broadcastOnly"] = Always("updated"),
                    ["hidden"] = Defer(() => 1),
                });
                Assert.Equal("updated", payload.GetProperty("broadcastOnly").GetString());
                return "page";
            }),
        }));
        Assert.Same(capture, http.Features.Get<DevToolsCapture>());
        Assert.Equal("Page", capture.Component);
        Assert.DoesNotContain("broadcastOnly", capture.PropMetadata.Keys);
        Assert.DoesNotContain("hidden", capture.PropMetadata.Keys);
        Assert.Contains("order", capture.PropMetadata.Keys);
        Assert.DoesNotContain("broadcastOnly", Encoding.UTF8.GetString(capture.PageJson!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entries_expire_and_global_pruning_preserves_the_newest_across_tabs()
    {
        var clock = new RecorderClock();
        var harness = new Harness(timeProvider: clock, environment: "Development", configureServices: services =>
            services.AddInertiaDevTools(options => { options.MaxEntries = 2; options.MaxAge = TimeSpan.FromMinutes(1); }));
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        for (var i = 0; i < 3; i++)
        {
            var http = harness.Context("/entry/" + i);
            http.Request.Headers["X-Inertia-Devtools-Tab"] = "tab" + i;
            await recorder.CompleteAsync(http, Capture(), false);
            clock.Now += TimeSpan.FromSeconds(1);
        }
        Assert.Equal(2, recorder.Count);
        var list = await ListAsync(harness);
        Assert.Equal(2, list.Count);
        Assert.EndsWith("/entry/2", (string?)list[0]!["url"], StringComparison.Ordinal);
        Assert.EndsWith("/entry/1", (string?)list[1]!["url"], StringComparison.Ordinal);
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Empty(await ListAsync(harness));
        Assert.Equal(0, recorder.Count);
        Assert.Equal(0, recorder.RetainedBytes);
    }

    [Theory]
    [InlineData("?offset=-1&limit=invalid", 2)]
    [InlineData("?offset=invalid&limit=-1", 2)]
    [InlineData("?offset=999999999999999999999&limit=0", 0)]
    [InlineData("?offset=1&limit=1", 1)]
    public async Task List_pagination_is_safe_for_malformed_values_and_equal_timestamps(string query, int count)
    {
        var harness = new Harness(timeProvider: new RecorderClock(), environment: "Development",
            configureServices: services => services.AddInertiaDevTools());
        var oldest = await RecordAsync(harness, harness.Context());
        var newest = await RecordAsync(harness, harness.Context());
        var first = await ListAsync(harness);
        var again = await ListAsync(harness);
        Assert.Equal((string?)newest["__meta"]!["id"], (string?)first[0]!["id"]);
        Assert.Equal((string?)oldest["__meta"]!["id"], (string?)first[1]!["id"]);
        Assert.Equal(first.ToJsonString(), again.ToJsonString());
        Assert.Equal(count, (await ListAsync(harness, query)).Count);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("../../../etc/passwd")]
    [InlineData("")]
    public async Task Unknown_or_malformed_entry_ids_return_not_found(string id)
    {
        var harness = Recording();
        var read = harness.Context();
        read.Request.Host = new HostString("localhost");
        read.Request.RouteValues["id"] = id;
        await harness.Services.GetRequiredService<DevToolsRecorder>().GetEntryAsync(read);
        Assert.Equal(404, read.Response.StatusCode);
        Assert.Empty(Harness.Body(read));
    }

    private static async Task<JsonArray> ListAsync(Harness harness, string query = "")
    {
        var read = harness.Context("/_inertia/devtools/entries" + query);
        read.Request.Host = new HostString("localhost");
        await harness.Services.GetRequiredService<DevToolsRecorder>().ListEntriesAsync(read);
        return JsonNode.Parse(Harness.Body(read))!.AsArray();
    }

    private sealed class RecorderClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static DevToolsCapture Capture(bool deferred = false, int limit = 1024) =>
        new(Guid.NewGuid().ToString("N"), Stopwatch.GetTimestamp(), limit) { DeferredRequest = deferred };

    private static Harness Recording(Action<InertiaDevToolsOptions>? configure = null) =>
        new(environment: "Development", configureServices: services => services.AddInertiaDevTools(configure));

    private static async Task<JsonObject> RecordAsync(Harness harness, HttpContext http, DevToolsCapture? capture = null)
    {
        var recorder = harness.Services.GetRequiredService<DevToolsRecorder>();
        capture ??= Capture();
        await recorder.CompleteAsync(http, capture, false);
        var read = harness.Context();
        read.Request.Host = new HostString("localhost");
        read.Request.RouteValues["id"] = capture.Id;
        await recorder.GetEntryAsync(read);
        return JsonNode.Parse(Harness.Body(read))!.AsObject();
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override long Position { get => throw new IOException("unavailable"); set => throw new IOException("unavailable"); }
    }
}
