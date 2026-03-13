using System.Buffers;

namespace Csharp14.SystemsMemory.StackSpan.Examples;

internal static class PooledBufferDemo
{
    private const int BatchBufferSize = 32 * 1024;
    private const int HeaderSize = 8;

    public static void Run()
    {
        Console.WriteLine("Large temporary telemetry batch rented from ArrayPool:");
        SendTelemetryBatch(sequence: 17, eventCount: 3);
    }

    private static void SendTelemetryBatch(int sequence, int eventCount)
    {
        using var buffer = new PooledBuffer<byte>(BatchBufferSize);
        Span<byte> batch = buffer.Span;

        batch[0] = 1;
        batch[1] = 0x42;
        BitConverter.TryWriteBytes(batch.Slice(2, 2), checked((ushort)eventCount));

        Span<byte> payload = batch.Slice(HeaderSize, eventCount * 8);
        BitConverter.TryWriteBytes(batch.Slice(4, 4), payload.Length);

        WriteTelemetryEvent(payload.Slice(0, 8), metricId: 42, value: 1250);
        WriteTelemetryEvent(payload.Slice(8, 8), metricId: 43, value: 980);
        WriteTelemetryEvent(payload.Slice(16, 8), metricId: 44, value: 1505);

        TelemetryBatchSinkWrite(batch[..(HeaderSize + payload.Length)], sequence);
    }

    private static void WriteTelemetryEvent(Span<byte> destination, int metricId, int value)
    {
        BitConverter.TryWriteBytes(destination[..4], metricId);
        BitConverter.TryWriteBytes(destination[4..8], value);
    }

    private static void TelemetryBatchSinkWrite(ReadOnlySpan<byte> batch, int sequence)
    {
        Console.WriteLine($"sequence={sequence}, bytes={batch.Length}");
        Console.WriteLine(Convert.ToHexString(batch));
    }
}

internal ref struct PooledBuffer<T>
{
    private T[]? _array;
    private readonly int _length;

    public PooledBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        _array = ArrayPool<T>.Shared.Rent(length);
        _length = length;
    }

    public Span<T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_array is null, typeof(PooledBuffer<T>));
            return _array.AsSpan(0, _length);
        }
    }

    public void Dispose()
    {
        if (_array is null)
        {
            return;
        }

        ArrayPool<T>.Shared.Return(_array);
        _array = null;
    }
}
