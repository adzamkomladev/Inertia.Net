using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inertia.Net.Tests;

/// <summary>Paths the coverage report showed untested: how the middleware learns that a response body was written, and per-mount SSR health.</summary>
public sealed class CoverageGapTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ResponseWriteTracker Tracker(MemoryStream body) => new(new StreamResponseBodyFeature(body));

    [Fact]
    public async Task Every_way_of_writing_a_body_counts_and_empty_writes_do_not()
    {
        using var body = new MemoryStream();
        var tracker = Tracker(body);
        tracker.Stream.Write([]);
        tracker.Stream.Write(new byte[0], 0, 0);
        await tracker.Stream.WriteAsync(ReadOnlyMemory<byte>.Empty, Ct);
        tracker.Writer.Advance(0);
        Assert.False(tracker.HasWritten);

        tracker.Stream.Write([1]);
        Assert.True(tracker.HasWritten);
        Assert.True(Tracker(body).Also(t => t.Stream.Write([1, 2], 0, 2)).HasWritten);
        Assert.True((await Tracker(body).AlsoAsync(t => t.Stream.WriteAsync(new byte[] { 1 }, 0, 1, Ct))).HasWritten);
        var advanced = Tracker(body);
        advanced.Writer.GetSpan(1)[0] = 1;
        advanced.Writer.Advance(1);
        await advanced.Writer.FlushAsync(Ct);
        Assert.True(advanced.HasWritten);
        Assert.Equal(1L + 2 + 1 + 1, body.Length);
    }

    [Fact]
    public void A_page_writes_to_the_server_writer_unless_the_stream_writer_is_already_in_use()
    {
        using var body = new MemoryStream();
        var inner = new StreamResponseBodyFeature(body);
        var tracker = new ResponseWriteTracker(inner);
        Assert.Same(inner.Writer, tracker.PageWriter);
        Assert.True(tracker.HasWritten);

        var used = new ResponseWriteTracker(inner);
        Assert.Same(used.Writer, used.PageWriter);
    }

    [Fact]
    public async Task Sent_files_count_unless_the_range_is_empty()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3], Ct);
            using var emptyBody = new MemoryStream();
            var empty = Tracker(emptyBody);
            await empty.SendFileAsync(path, 3, null, Ct);
            await empty.SendFileAsync(path, 0, 0, Ct);
            Assert.False(empty.HasWritten);
            using var sentBody = new MemoryStream();
            var sent = Tracker(sentBody);
            await sent.SendFileAsync(path, 1, null, Ct);
            Assert.True(sent.HasWritten);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task The_tracked_body_passes_everything_else_through()
    {
        using var body = new MemoryStream([1, 2, 3]);
        var tracker = Tracker(body);
        tracker.DisableBuffering();
        var stream = tracker.Stream;
        Assert.True(stream.CanRead && stream.CanSeek && stream.CanWrite);
        Assert.Equal(3, stream.Length);
        stream.Position = 1;
        Assert.Equal(1, stream.Position);
        var read = new byte[2];
        Assert.Equal(2, stream.Read(read, 0, 2));
        Assert.Equal([2, 3], read);
        Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));
        stream.SetLength(1);
        stream.Flush();
        await stream.FlushAsync(Ct);
        Assert.Equal(1, body.Length);
        Assert.False(tracker.HasWritten);

        tracker.Writer.CancelPendingFlush();
        tracker.Writer.Complete();
        using var completed = new MemoryStream();
        await Tracker(completed).Writer.CompleteAsync();
        await tracker.CompleteAsync();
    }

    [Fact]
    public async Task The_ssr_response_stream_reads_up_to_its_limit_and_nothing_else()
    {
        using var stream = new SsrResponseStream(new MemoryStream([1, 2, 3, 4, 5, 6]), 4);
        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek || stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Position = 1);
        Assert.Throws<NotSupportedException>(stream.Flush);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));

        var buffer = new byte[3];
        Assert.Equal(3, stream.Read(buffer, 0, 3));
        Assert.Equal(1, await stream.ReadAsync(buffer, 0, 1, Ct));
        Assert.Equal(4, stream.Position);
        await Assert.ThrowsAsync<SsrResponseTooLargeException>(() => stream.ReadAsync(buffer, 0, 3, Ct));

        using var unlimited = new SsrResponseStream(new MemoryStream([1, 2]), null);
        Assert.Equal(2, await unlimited.ReadAsync(new byte[8], 0, 8, Ct));
    }

    [Fact]
    public async Task A_mount_gets_a_health_check_of_its_own()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new TestHostEnvironment(Path.GetTempPath()));
        services.AddInertia();
        services.AddInertia("admin", _ => { });
        services.AddHealthChecks().AddInertiaSsrForMount("admin").AddInertiaSsrForMount("", "root");
        await using var app = services.BuildServiceProvider();
        var report = await app.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
        Assert.Equal(["inertia-ssr-admin", "root"], report.Entries.Keys.Order());
        Assert.All(report.Entries.Values, e => Assert.Equal("Inertia SSR is disabled.", e.Description));
    }
}

internal static class TrackerExtensions
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }

    public static async Task<T> AlsoAsync<T>(this T value, Func<T, Task> act)
    {
        await act(value);
        return value;
    }
}
