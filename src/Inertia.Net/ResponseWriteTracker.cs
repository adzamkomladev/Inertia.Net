using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace Inertia.Net;

// HasStarted and Content-Length do not indicate whether buffering middleware has received a body.
internal sealed class ResponseWriteTracker(IHttpResponseBodyFeature inner) : IHttpResponseBodyFeature
{
    private Stream? _stream;
    private PipeWriter? _writer;
    internal bool HasWritten { get; private set; }
    public Stream Stream => _stream ??= new TrackingStream(inner.Stream, this);
    public PipeWriter Writer => _writer ??= new TrackingWriter(inner.Writer, this);
    public void DisableBuffering() => inner.DisableBuffering();
    public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
    public Task CompleteAsync() => inner.CompleteAsync();
    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        HasWritten |= (count ?? new FileInfo(path).Length - offset) > 0;
        return inner.SendFileAsync(path, offset, count, cancellationToken);
    }

    private sealed class TrackingWriter(PipeWriter inner, ResponseWriteTracker owner) : PipeWriter
    {
        public override void Advance(int bytes) { inner.Advance(bytes); owner.HasWritten |= bytes > 0; }
        public override Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);
        public override void CancelPendingFlush() => inner.CancelPendingFlush();
        public override void Complete(Exception? exception = null) => inner.Complete(exception);
        public override ValueTask CompleteAsync(Exception? exception = null) => inner.CompleteAsync(exception);
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
        public override bool CanGetUnflushedBytes => inner.CanGetUnflushedBytes;
        public override long UnflushedBytes => inner.UnflushedBytes;
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
        public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); owner.HasWritten |= count > 0; }
        public override void Write(ReadOnlySpan<byte> buffer) { inner.Write(buffer); owner.HasWritten |= buffer.Length > 0; }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            owner.HasWritten |= buffer.Length > 0;
        }
    }
}
