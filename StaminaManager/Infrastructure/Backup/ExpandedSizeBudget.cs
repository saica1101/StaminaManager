namespace StaminaManager.Infrastructure.Backup;

public sealed class ExpandedSizeBudget
{
    private readonly long _maxBytes;

    public ExpandedSizeBudget(long maxBytes)
    {
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        _maxBytes = maxBytes;
    }

    public long ConsumedBytes { get; private set; }

    public void Consume(int byteCount)
    {
        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        if (byteCount > _maxBytes - ConsumedBytes)
        {
            throw new InvalidDataException(
                "The expanded backup is too large.");
        }

        ConsumedBytes += byteCount;
    }
}

internal sealed class BudgetedWriteStream(
    Stream inner,
    ExpandedSizeBudget budget) : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        budget.Consume(count);
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        budget.Consume(buffer.Length);
        inner.Write(buffer);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        budget.Consume(count);
        await inner.WriteAsync(
            buffer.AsMemory(offset, count),
            cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        budget.Consume(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
    }
}
