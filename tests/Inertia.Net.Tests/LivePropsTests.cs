using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Inertia.Net.Tests;

/// <summary>Live props: channel and event types, the fluent API and factories, and publishing through <see cref="IInertiaLive"/>.</summary>
public class LivePropsTests
{
    private static readonly LiveEvent OrderUpdated = new("OrderUpdated", LiveChannel.Private("orders.1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Live_dot_parent_keeps_its_listener()
    {
        var harness = new Harness();
        var page = await harness.PageAsync(new InertiaProps
        {
            ["auth"] = Inertia.Prop(() => new InertiaProps { ["name"] = "Ada" }).Live(OrderUpdated),
            ["auth.role"] = "admin",
        });
        Assert.NotNull(page["liveProps"]?["auth"]);
        JsonAssert.Equal("""{"name":"Ada","role":"admin"}""", page["props"]!["auth"]);
    }

    [Theory]
    [InlineData(LiveChannelType.Public, "public")]
    [InlineData(LiveChannelType.Private, "private")]
    [InlineData(LiveChannelType.Presence, "presence")]
    [InlineData(LiveChannelType.EncryptedPrivate, "encrypted-private")]
    public void Channel_types_have_protocol_names_and_keys(LiveChannelType type, string name)
    {
        var channel = new LiveChannel("orders.1", type);
        Assert.Equal(name, channel.TypeName);
        Assert.Equal($"{name}:orders.1", channel.Key);
        Assert.True(LiveChannel.TryParseType(name, out var parsed));
        Assert.Equal(type, parsed);
    }

    [Fact]
    public void Channel_factories_set_the_type()
    {
        Assert.Equal(LiveChannelType.Public, LiveChannel.Public("a").Type);
        Assert.Equal(LiveChannelType.Private, LiveChannel.Private("a").Type);
        Assert.Equal(LiveChannelType.Presence, LiveChannel.Presence("a").Type);
        Assert.Equal(LiveChannelType.EncryptedPrivate, LiveChannel.EncryptedPrivate("a").Type);
        Assert.Equal(LiveChannel.Private("a"), new LiveChannel("a", LiveChannelType.Private));
        Assert.False(LiveChannel.TryParseType("Private", out _));
        Assert.False(LiveChannel.TryParseType(null, out _));
    }

    [Fact]
    public void Names_are_validated()
    {
        Assert.ThrowsAny<ArgumentException>(() => LiveChannel.Public(""));
        Assert.ThrowsAny<ArgumentException>(() => LiveChannel.Private(null!));
        Assert.ThrowsAny<ArgumentException>(() => new LiveChannel("a", (LiveChannelType)42));
        Assert.ThrowsAny<ArgumentException>(() => new LiveEvent("", LiveChannel.Public("a")));
        Assert.ThrowsAny<ArgumentException>(() => new LiveEvent("E", default));
        Assert.ThrowsAny<ArgumentException>(() => Inertia.Prop(() => 1).Live(LiveChannel.Public("a")));
        Assert.ThrowsAny<ArgumentException>(() => Inertia.Prop(() => 1).Live(LiveChannel.Public("a"), "E", ""));
        Assert.ThrowsAny<ArgumentException>(() => Inertia.Prop(() => 1).Live(default, "E"));
    }

    [Fact]
    public void Live_calls_accumulate()
    {
        var prop = Inertia.Prop(() => 1).Live(OrderUpdated).Live(LiveChannel.Public("stats"), "A", "B");
        Assert.Equal(
            [(LiveChannel.Private("orders.1"), "OrderUpdated"), (LiveChannel.Public("stats"), "A"), (LiveChannel.Public("stats"), "B")],
            prop.LiveSpec!.Listeners);
    }

    [Fact]
    public void Merge_and_live_do_not_combine_in_either_order()
    {
        Assert.Throws<InvalidOperationException>(() => Inertia.Merge(new[] { 1 }).Live(OrderUpdated));
        Assert.Throws<InvalidOperationException>(() => Inertia.DeepMerge(new { a = 1 }).Live(OrderUpdated));
        Assert.Throws<InvalidOperationException>(() => Inertia.Scroll(new PagedUsers([], 1, false)).Live(OrderUpdated));
        Assert.Throws<InvalidOperationException>(() => Inertia.Prop(() => 1).Live(OrderUpdated).Append());
        Assert.Throws<InvalidOperationException>(() => Inertia.Prop(() => 1).Live(OrderUpdated).Prepend("a"));
        Assert.Throws<InvalidOperationException>(() => Inertia.Prop(() => 1).Live(OrderUpdated).DeepMerge());
        Assert.Throws<InvalidOperationException>(() => Inertia.Prop(() => 1).Live(OrderUpdated).MatchOn("id"));
    }

    [Fact]
    public void Throttle_needs_a_live_prop_and_a_positive_window()
    {
        Assert.Throws<InvalidOperationException>(() => Inertia.Prop(() => 1).Throttle(TimeSpan.FromSeconds(1)));
        Assert.ThrowsAny<ArgumentException>(() => Inertia.Prop(() => 1).Live(OrderUpdated).Throttle(TimeSpan.Zero));
        Assert.Equal(1500, Inertia.Prop(() => 1).Live(OrderUpdated).Throttle(TimeSpan.FromSeconds(1.5)).LiveSpec!.ThrottleMs);
    }

    [Fact]
    public void Factories_produce_live_props()
    {
        InertiaProp[] props =
        [
            Inertia.Live(1, OrderUpdated),
            Inertia.Live(() => 1, OrderUpdated),
            Inertia.Live(() => Task.FromResult(1), OrderUpdated),
            Inertia.Live(() => ValueTask.FromResult(1), OrderUpdated),
            Inertia.Live(_ => Task.FromResult(1), OrderUpdated),
            Inertia.Live(_ => ValueTask.FromResult(1), OrderUpdated),
        ];

        Assert.All(props, p => Assert.Equal([(OrderUpdated.Channel, OrderUpdated.Name)], p.LiveSpec!.Listeners));
        Assert.All(props, p => Assert.Equal(typeof(int), p.ValueType));
    }

    [Fact]
    public async Task Publishing_without_values_sends_the_event_only()
    {
        var (live, broadcaster) = Live();
        await live.PublishAsync(OrderUpdated, exceptSocketId: "socket-1", cancellationToken: Ct);

        var message = Assert.Single(broadcaster.Messages);
        Assert.Equal(OrderUpdated.Channel, message.Channel);
        Assert.Equal("OrderUpdated", message.Event);
        Assert.Equal("socket-1", message.ExceptSocketId);
        Assert.Equal("""{"__inertia":{"event":"OrderUpdated"}}""", message.Payload.GetRawText());
    }

    [Fact]
    public async Task Published_values_use_the_page_naming_policy_and_keep_dot_keys_flat()
    {
        var (live, broadcaster) = Live(services => services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase));
        await live.PublishAsync(OrderUpdated, new { Order = new User("Ada"), Count = 2 }, cancellationToken: Ct);
        await live.PublishAsync(OrderUpdated, new Dictionary<string, object?> { ["auth.user"] = new User("Ada") }, cancellationToken: Ct);
        await live.PublishAsync<object?>(OrderUpdated, null, cancellationToken: Ct);

        Assert.Equal("""{"__inertia":{"event":"OrderUpdated","props":{"order":{"name":"Ada","email":null},"count":2}}}""", broadcaster.Messages[0].Payload.GetRawText());
        Assert.Equal("""{"__inertia":{"event":"OrderUpdated","props":{"auth.user":{"name":"Ada","email":null}}}}""", broadcaster.Messages[1].Payload.GetRawText());
        Assert.Equal("""{"__inertia":{"event":"OrderUpdated"}}""", broadcaster.Messages[2].Payload.GetRawText());
        Assert.Null(broadcaster.Messages[0].ExceptSocketId);
    }

    [Fact]
    public async Task Published_values_can_use_source_generated_metadata()
    {
        var (live, broadcaster) = Live();
        await live.PublishAsync(OrderUpdated, new OrderProps(new User("Ada")), LiveTestJsonContext.Default.OrderProps, "socket-2", Ct);

        var message = Assert.Single(broadcaster.Messages);
        Assert.Equal("""{"__inertia":{"event":"OrderUpdated","props":{"order":{"Name":"Ada","Email":null}}}}""", message.Payload.GetRawText());
        Assert.Equal("socket-2", message.ExceptSocketId);
    }

    [Fact]
    public async Task Published_values_must_be_plain_objects()
    {
        var (live, _) = Live();
        await Assert.ThrowsAsync<ArgumentException>(() => live.PublishAsync(OrderUpdated, new InertiaProps { ["order"] = 1 }, cancellationToken: Ct).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => live.PublishAsync(OrderUpdated, new[] { 1 }, cancellationToken: Ct).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => live.PublishAsync(OrderUpdated, 5, cancellationToken: Ct).AsTask());
    }

    [Fact]
    public async Task Publishing_without_a_broadcaster_throws()
    {
        var live = new Harness(configureServices: services => services.AddInertiaLive()).Services.GetRequiredService<IInertiaLive>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => live.PublishAsync(OrderUpdated, cancellationToken: Ct).AsTask());
        Assert.Contains(nameof(IInertiaLiveBroadcaster), error.Message);
    }

    [Fact]
    public void AddInertiaLive_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddInertia();
        var builder = services.AddInertiaLive();
        services.AddInertiaLive();

        Assert.Same(services, builder.Services);
        Assert.Single(services, d => d.ServiceType == typeof(IInertiaLive));
    }

    private static (IInertiaLive Live, RecordingBroadcaster Broadcaster) Live(Action<IServiceCollection>? configure = null)
    {
        var broadcaster = new RecordingBroadcaster();
        var harness = new Harness(configureServices: services =>
        {
            configure?.Invoke(services);
            services.AddInertiaLive();
            services.AddSingleton<IInertiaLiveBroadcaster>(broadcaster);
        });

        return (harness.Services.GetRequiredService<IInertiaLive>(), broadcaster);
    }

    private sealed class RecordingBroadcaster : IInertiaLiveBroadcaster
    {
        public List<LiveMessage> Messages { get; } = [];

        public ValueTask PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }
}

public sealed record OrderProps([property: JsonPropertyName("order")] User Order);

[JsonSerializable(typeof(OrderProps))]
internal sealed partial class LiveTestJsonContext : JsonSerializerContext;
