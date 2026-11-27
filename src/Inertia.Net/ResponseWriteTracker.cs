using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace Inertia.Net;

// HasStarted and Content-Length do not indicate whether buffering middleware has received a body.
internal sealed class ResponseWriteTracker(IHttpResponseBodyFeature inner, DevToolsCapture? capture = null) : IHttpResponseBodyFeature
{
    private static readonly OperationCanceledException ScopeEnded = new("Response writer scope ended.");
    private Stream? _stream;
    private PipeWriter? _writer;
    internal bool HasWritten { get; private set; }
    internal bool SuppressBody { get; set; }
    private void Record(ReadOnlySpan<byte> bytes) => capture?.RecordResponse(bytes);
    public Stream Stream => _stream ??= new TrackingStream(inner.Stream, this);
    public PipeWriter Writer => _writer ??= PipeWriter.Create(Stream, new StreamPipeWriterOptions(leaveOpen: true));
    // A rendered page is never a redirect whose body is dropped, so it skips the tracking stream and its second buffer.
    // A stream writer that was already handed out is reused, so earlier writes stay in order.
    internal PipeWriter PageWriter
    {
        get
        {
            HasWritten = true;
            return _writer ?? inner.Writer;
        }
    }
    public void DisableBuffering()
    {
        if (capture is not null) capture.ResponseStreamed = true;
        inner.DisableBuffering();
    }
    public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
    internal async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        if (_writer is { UnflushedBytes: > 0 }) await _writer.FlushAsync(cancellationToken);
    }
    // Return the wrapper's pooled pipe buffers without closing the server-owned response stream.
    internal ValueTask ReleaseAsync() => _writer?.CompleteAsync(ScopeEnded) ?? ValueTask.CompletedTask;
    public async Task CompleteAsync()
    {
        await DrainAsync();
        await inner.CompleteAsync();
    }
    public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        await inner.StartAsync(cancellationToken);
        if (SuppressBody) return;
        if (capture is not null) capture.ResponseStreamed = true;
        HasWritten |= (count ?? new FileInfo(path).Length - offset) > 0;
        await inner.SendFileAsync(path, offset, count, cancellationToken);
    }

    private sealed class TrackingStream(Stream inner, ResponseWriteTracker owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            owner.StartAsync().GetAwaiter().GetResult();
            if (owner.SuppressBody) return;
            inner.Write(buffer);
            owner.HasWritten |= buffer.Length > 0;
            owner.Record(buffer);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await owner.StartAsync(cancellationToken);
            if (owner.SuppressBody) return;
            await inner.WriteAsync(buffer, cancellationToken);
            owner.HasWritten |= buffer.Length > 0;
            owner.Record(buffer.Span);
        }
    }
}
