namespace Inertia.Net;

internal sealed class SsrResponseTooLargeException(long limit) : IOException($"The SSR response exceeds the {limit}-byte limit.");

// Reads at most one byte beyond the limit, including responses without a trustworthy Content-Length.
internal sealed class SsrResponseStream(Stream inner, long? limit) : Stream
{
    private long _read;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer[..ReadLength(buffer.Length)]));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer[..ReadLength(buffer.Length)], cancellationToken));

    private int ReadLength(int requested) => limit is { } max && max - _read < requested
        ? (int)Math.Min(requested, max - _read + 1) : requested;

    private int Count(int count)
    {
        _read += count;
        if (limit is { } max && _read > max) throw new SsrResponseTooLargeException(max);
        return count;
    }
}
