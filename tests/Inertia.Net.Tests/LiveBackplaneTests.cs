using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Inertia.Net.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inertia.Net.Tests;

/// <summary>The backplane seam in core: the relay broadcaster, its subscription, the wire format and the backplane metrics.</summary>
public sealed class LiveBackplaneTests
{
    private static readonly LiveEvent OrderUpdated = new("OrderUpdated", LiveChannel.Private("orders.1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingTransport : IInertiaLiveLocalTransport
    {
        public List<LiveMessage> Delivered { get; } = [];

        public int Resyncs { get; private set; }

        public ValueTask DeliverAsync(LiveMessage message, CancellationToken cancellationToken = default)
        {
            lock (Delivered) Delivered.Add(message);
            return default;
        }

        public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
        {
            Resyncs++;
            return default;
        }
    }

    private static ServiceProvider Services(FakeBackplane? backplane, RecordingTransport? transport, Action<IServiceCollection>? configure = null, bool transportFirst = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        void AddTransport()
        {
            if (transport is not null) services.AddSingleton<IInertiaLiveLocalTransport>(transport);
        }

        void AddBackplane()
        {
            if (backplane is not null) services.AddSingleton<IInertiaLiveBackplane>(backplane);
        }

        if (transportFirst)
        {
            AddTransport();
            services.AddInertiaLive();
            AddBackplane();
        }
        else
        {
            AddBackplane();
            services.AddInertiaLive();
            AddTransport();
        }

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(IServiceProvider services)
    {
        foreach (var hosted in services.GetServices<IHostedService>()) await hosted.StartAsync(Ct);
    }

    [Fact]
    public async Task A_publish_goes_through_the_backplane_once_with_this_instance_id_and_comes_back_once()
    {
        var backplane = new FakeBackplane();
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport);
        await StartAsync(services);

        await services.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, "socket-1", Ct);

        var local = Assert.Single(transport.Delivered);
        Assert.Equal("socket-1", local.ExceptSocketId);
        var wire = Assert.Single(backplane.Published);
        Assert.True(LiveWire.TryRead(wire, out _, out var origin, out _));
        Assert.Equal(services.GetRequiredService<LiveBackplaneRelay>().InstanceId, origin);
    }

    [Fact]
    public async Task Every_instance_the_publisher_included_delivers_each_message_once_from_the_backplane()
    {
        var backplane = new FakeBackplane();
        var (a, b) = (new RecordingTransport(), new RecordingTransport());
        await using var hostA = Services(backplane, a);
        await using var hostB = Services(backplane, b);
        await StartAsync(hostA);
        await StartAsync(hostB);

        await hostA.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, new Dictionary<string, int> { ["total"] = 3 }, cancellationToken: Ct);

        Assert.Single(a.Delivered); // from the backplane, not also directly
        var remote = Assert.Single(b.Delivered);
        Assert.Equal("OrderUpdated", remote.Event);
        Assert.Equal(3, remote.Payload.GetProperty("__inertia").GetProperty("props").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Concurrent_publishes_from_two_instances_reach_both_in_the_same_order()
    {
        var backplane = new FakeBackplane();
        var (a, b) = (new RecordingTransport(), new RecordingTransport());
        await using var hostA = Services(backplane, a);
        await using var hostB = Services(backplane, b);
        await StartAsync(hostA);
        await StartAsync(hostB);
        var (liveA, liveB) = (hostA.GetRequiredService<IInertiaLive>(), hostB.GetRequiredService<IInertiaLive>());

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
            await (i % 2 == 0 ? liveA : liveB).PublishAsync(OrderUpdated, new Dictionary<string, int> { ["n"] = i }, cancellationToken: Ct), Ct)));

        // Before 1.10 each instance delivered its own publish at once, so the last value could differ between them.
        static int[] Values(RecordingTransport t) => [.. t.Delivered.Select(m => m.Payload.GetProperty("__inertia").GetProperty("props").GetProperty("n").GetInt32())];
        Assert.Equal(50, a.Delivered.Count);
        Assert.Equal(Values(a), Values(b));
    }

    [Fact]
    public async Task With_a_running_subscription_a_failed_publish_reaches_no_client()
    {
        var backplane = new FakeBackplane { PublishFailure = new InvalidOperationException("store down") };
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport);
        await StartAsync(services);

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, cancellationToken: Ct).AsTask());

        Assert.Empty(transport.Delivered);
    }

    [Fact]
    public async Task A_message_over_the_limit_even_without_values_fails_the_publish_and_is_counted()
    {
        var backplane = new FakeBackplane { MaxMessageBytes = 150 };
        await using var services = Services(backplane, new RecordingTransport());
        using var counts = Count(services);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, new string('s', 200), Ct).AsTask());

        Assert.Empty(backplane.Published);
        Assert.Equal("oversize", counts.Tag("inertia.live.backplane.errors", "error.type"));
    }

    [Fact]
    public void An_IInertiaLive_written_before_CreateMessage_existed_still_works()
    {
        IInertiaLive live = new LegacyLive();

        Assert.Equal("OrderUpdated", live.CreateMessage(OrderUpdated, "s").Payload.GetProperty("__inertia").GetProperty("event").GetString());
        Assert.Contains("Ann", live.CreateMessage(OrderUpdated, new OrderProps(new User("Ann")), LiveTestJsonContext.Default.OrderProps)
            .Payload.GetProperty("__inertia").GetProperty("props").GetProperty("order").GetRawText(), StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => live.CreateMessage(OrderUpdated, new OrderProps(new User("Ann"))));
    }

    private sealed class LegacyLive : IInertiaLive
    {
        public ValueTask PublishAsync(LiveEvent @event, string? exceptSocketId = null, CancellationToken cancellationToken = default) => default;

        public ValueTask PublishAsync<TProps>(LiveEvent @event, TProps props, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TProps> propsTypeInfo, string? exceptSocketId = null,
            CancellationToken cancellationToken = default) => default;

        public ValueTask PublishAsync<TProps>(LiveEvent @event, TProps props, string? exceptSocketId = null, CancellationToken cancellationToken = default) => default;
    }

    [Fact]
    public async Task A_gap_resyncs_the_transports_once_and_is_counted()
    {
        var backplane = new FakeBackplane();
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport);
        using var counts = Count(services);
        await StartAsync(services);

        backplane.SimulateGap();
        await WaitUntil(() => transport.Resyncs == 1);

        Assert.Equal(1, counts.Get("inertia.live.backplane.resyncs"));
        Assert.Equal("custom", counts.Tag("inertia.live.backplane.resyncs", "inertia.live.backplane"));
    }

    [Fact]
    public async Task A_worker_without_a_transport_or_AddInertia_publishes_and_does_not_subscribe()
    {
        var backplane = new FakeBackplane();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddInertiaLive();
        services.AddSingleton<IInertiaLiveBackplane>(backplane);
        await using var provider = services.BuildServiceProvider();
        await StartAsync(provider);

        await provider.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, cancellationToken: Ct);

        Assert.Single(backplane.Published);
        Assert.Equal(0, backplane.SubscriberCount);
    }

    [Fact]
    public async Task A_broadcaster_the_app_registers_still_wins()
    {
        var backplane = new FakeBackplane();
        var custom = new CustomBroadcaster();
        await using var services = Services(backplane, new RecordingTransport(), s => s.AddSingleton<IInertiaLiveBroadcaster>(custom));

        await services.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, cancellationToken: Ct);

        Assert.Equal(1, custom.Count);
        Assert.Empty(backplane.Published);
    }

    [Fact]
    public async Task Registration_order_does_not_matter()
    {
        var backplane = new FakeBackplane();
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport, transportFirst: false);
        await StartAsync(services);

        await services.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, cancellationToken: Ct);

        Assert.Single(transport.Delivered);
        Assert.Single(backplane.Published);
        Assert.Equal(1, backplane.SubscriberCount);
    }

    [Fact]
    public async Task An_over_limit_message_goes_out_without_values_and_is_counted()
    {
        var backplane = new FakeBackplane { MaxMessageBytes = 200 };
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport);
        using var counts = Count(services);

        await services.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, new Dictionary<string, string> { ["note"] = new('x', 500) }, cancellationToken: Ct);

        Assert.True(Assert.Single(transport.Delivered).Payload.GetProperty("__inertia").TryGetProperty("props", out _)); // local keeps values
        Assert.True(LiveWire.TryRead(Assert.Single(backplane.Published), out var sent, out _, out _));
        Assert.False(sent.Payload.GetProperty("__inertia").TryGetProperty("props", out _));
        Assert.Equal(1, counts.Get("inertia.live.backplane.oversize"));
        Assert.Equal("reload", counts.Tag("inertia.live.backplane.oversize", "outcome"));
    }

    [Fact]
    public async Task A_failed_publish_surfaces_after_local_delivery_and_is_counted()
    {
        var backplane = new FakeBackplane { PublishFailure = new InvalidOperationException("store down") };
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport);
        using var counts = Count(services);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IInertiaLive>().PublishAsync(OrderUpdated, cancellationToken: Ct).AsTask());

        Assert.Equal("store down", error.Message);
        Assert.Single(transport.Delivered);
        Assert.Equal(1, counts.Get("inertia.live.backplane.errors"));
        Assert.Equal(0, counts.Get("inertia.live.backplane.published"));
    }

    [Fact]
    public async Task Malformed_messages_are_dropped_and_counted()
    {
        var backplane = new FakeBackplane();
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport);
        using var counts = Count(services);
        await StartAsync(services);

        await backplane.InjectAsync(Encoding.UTF8.GetBytes("""{"v":2,"event":"E","channel":"public:a","payload":{"__inertia":{}}}"""));
        await backplane.InjectAsync(Encoding.UTF8.GetBytes("""{"v":1,"event":"E","chan"""));

        Assert.Empty(transport.Delivered);
        Assert.Equal(2, counts.Get("inertia.live.backplane.errors"));
        Assert.Equal("malformed", counts.Tag("inertia.live.backplane.errors", "error.type"));
    }

    [Fact]
    public async Task A_message_that_still_carries_a_reference_is_delivered_without_values_and_counted_missing()
    {
        var backplane = new FakeBackplane();
        var transport = new RecordingTransport();
        await using var services = Services(backplane, transport);
        using var counts = Count(services);
        await StartAsync(services);
        var live = services.GetRequiredService<IInertiaLive>();

        await backplane.InjectAsync(LiveWire.Write(LiveWire.WithoutValues(live.CreateMessage(OrderUpdated, new Dictionary<string, int> { ["a"] = 1 })), "other", reference: 42));

        Assert.Single(transport.Delivered);
        Assert.Equal(1, counts.Get("inertia.live.backplane.received"));
        Assert.Equal("missing", counts.Tag("inertia.live.backplane.oversize", "outcome"));
    }

    [Fact]
    public async Task CreateMessage_builds_what_PublishAsync_sends()
    {
        var transport = new RecordingTransport();
        await using var services = Services(null, transport, s => s.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, LiveTestJsonContext.Default)));
        var live = services.GetRequiredService<IInertiaLive>();
        var props = new OrderProps(new User("Ann"));

        await live.PublishAsync(OrderUpdated, "s", Ct);
        await live.PublishAsync(OrderUpdated, props, LiveTestJsonContext.Default.OrderProps, "s", Ct);
        await live.PublishAsync(OrderUpdated, props, "s", Ct);
        LiveMessage[] created =
        [
            live.CreateMessage(OrderUpdated, "s"),
            live.CreateMessage(OrderUpdated, props, LiveTestJsonContext.Default.OrderProps, "s"),
            live.CreateMessage(OrderUpdated, props, "s"),
        ];

        Assert.Equal(created.Select(Raw), transport.Delivered.Select(Raw));
        static string Raw(LiveMessage m) => $"{m.Event}|{m.Channel}|{m.ExceptSocketId}|{m.Payload.GetRawText()}";
    }

    [Fact]
    public void LiveWire_round_trips_every_field_including_unicode_and_nested_json()
    {
        var payload = JsonDocument.Parse("""{"__inertia":{"event":"Ünïcødé ✓","props":{"a.b":{"c":[1,{"d":"\"quoted\" \\ \u2028"}],"big":12345678901234567890}}}}""").RootElement;
        var message = new LiveMessage(new LiveEvent("Ünïcødé ✓", LiveChannel.EncryptedPrivate("users.é")), payload, "9f3c");

        Assert.True(LiveWire.TryRead(LiveWire.Write(message, "instance", 7), out var read, out var origin, out var reference));

        Assert.Equal(message.Event, read.Event);
        Assert.Equal(message.Channel, read.Channel);
        Assert.Equal("9f3c", read.ExceptSocketId);
        Assert.Equal(payload.GetRawText(), read.Payload.GetRawText());
        Assert.Equal("instance", origin);
        Assert.Equal(7, reference);
    }

    [Theory]
    [InlineData("""{"v":2,"event":"E","channel":"public:a","payload":{"__inertia":{}}}""")]
    [InlineData("""{"event":"E","channel":"public:a","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"event":"E","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"event":"E","channel":"nope:a","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","payload":[1]}""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","payload":{"x":1}}""")]
    [InlineData("""{"v":1,"from":5,"event":"E","channel":"public:a","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"from":"","event":"E","channel":"public:a","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"event":"","channel":"public:a","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","ref":"x","payload":{"__inertia":{}}}""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","payload":{"__inertia":{}""")]
    [InlineData("""[]""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","payload":{"__inertia":{"event":"E"}}} trailing""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","payload":{"__inertia":{"event":"E"}}}{}""")]
    [InlineData("""{"v":1,"event":"\ud800","channel":"public:a","payload":{"__inertia":{"event":"E"}}}""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","payload":{"__inertia":{"event":"Other"}}}""")]
    [InlineData("""{"v":1,"event":"E","channel":"public:a","payload":{"__inertia":{"event":"E","props":[1]}}}""")]
    public void LiveWire_rejects_invalid_messages(string json)
    {
        Assert.False(LiveWire.TryRead(Encoding.UTF8.GetBytes(json), out var message, out _, out _));
        Assert.Null(message);
    }

    [Fact]
    public void LiveWire_rejects_a_message_over_1_MiB()
    {
        var big = "{\"v\":1,\"event\":\"E\",\"channel\":\"public:a\",\"payload\":{\"__inertia\":{\"props\":{\"x\":\"" + new string('x', LiveWire.MaxMessageBytes) + "\"}}}}";
        Assert.False(LiveWire.TryRead(Encoding.UTF8.GetBytes(big), out _, out _, out _));
    }

    [Fact]
    public void LiveWire_ignores_unknown_members()
    {
        Assert.True(LiveWire.TryRead("""{"v":1,"event":"E","channel":"public:a","later":true,"payload":{"__inertia":{"event":"E"}}}"""u8, out var message, out var origin, out var reference));
        Assert.Equal("E", message.Event);
        Assert.Null(origin);
        Assert.Null(reference);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10, Ct);
        Assert.True(condition());
    }

    private sealed class CustomBroadcaster : IInertiaLiveBroadcaster
    {
        public int Count { get; private set; }

        public ValueTask PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
        {
            Count++;
            return default;
        }
    }

    // Counts the backplane instruments of one provider's meter (other tests' meters are ignored).
    private static Counts Count(IServiceProvider services) => new(services.GetRequiredService<IMeterFactory>());

    private sealed class Counts : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, long> _counts = new();
        private readonly ConcurrentDictionary<(string, string), string?> _tags = new();

        public Counts(IMeterFactory factory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, factory) && instrument.Name.StartsWith("inertia.live.backplane.", StringComparison.Ordinal)) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                _counts.AddOrUpdate(instrument.Name, value, (_, total) => total + value);
                foreach (var tag in tags) _tags[(instrument.Name, tag.Key)] = tag.Value?.ToString();
            });
            _listener.Start();
            factory.Create(InertiaDiagnostics.MeterName); // publish instruments created before the listener started
        }

        public long Get(string name) => _counts.GetValueOrDefault(name);

        public string? Tag(string name, string key) => _tags.GetValueOrDefault((name, key));

        public void Dispose() => _listener.Dispose();
    }
}
