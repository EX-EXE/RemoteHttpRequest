namespace RemoteHttpRequest.Shared;

public class WriteFuncStream : Stream
{
    private int maxWriteByteSize;
    private Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> writeTask;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotImplementedException();

    public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }


    public WriteFuncStream(
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> writeTask,
        int maxWriteByteSize = 1024 * 1024)
        : base()
    {
        this.writeTask = writeTask;
        this.maxWriteByteSize = maxWriteByteSize;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var memory = buffer.AsMemory().Slice(offset, count);
        WriteAsync(memory, default).GetAwaiter().GetResult();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remainingBuffer = buffer;
        while (0 < remainingBuffer.Length)
        {
            var writeSize = Math.Min(remainingBuffer.Length, maxWriteByteSize);
            var writeBuffer = remainingBuffer.Slice(0, writeSize);
            remainingBuffer = remainingBuffer.Slice(writeSize);
            await writeTask(writeBuffer, cancellationToken).ConfigureAwait(false);
        }
    }

    public override void Close()
    {
        base.Close();
    }
}

