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

    public static async Task RunAsync()
    {
        Console.WriteLine("ref struct cannot cross await, so serialize after the async read:");
        await SendTelemetryAsync();
    }

    // This would not compile because PooledBuffer<T> is a ref struct and an async
    // method may lift locals into a heap-allocated state machine:
    //
    // async Task SendAsync()
    // {
    //     using var buffer = new PooledBuffer<byte>(1024);
    //     await Task.Delay(1);
    //     Send(buffer.Span);
    // }

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

    // error CS4007: Instance of type 'System.Span<byte>' cannot be preserved across 'await' or 'yield' boundary.
    // private static async Task SendTelemetryBrokenAsync()
    // {
    //     using var buffer = new PooledBuffer<byte>(1024);
    //     var span = buffer.Span;

    //     var telemetryEvent = await ReadTelemetryEventAsync();
    //     var written = SerializeTelemetry(telemetryEvent, span);

    //     Send(span.Slice(0, written));
    // }
    private static async Task SendTelemetryAsync()
    {
        var telemetryEvent = await ReadTelemetryEventAsync();
        SerializeAndSend();

        void SerializeAndSend()
        {
            using var buffer = new PooledBuffer<byte>(1024);
            var span = buffer.Span;

            var written = SerializeTelemetry(telemetryEvent, span);
            Send(span.Slice(0, written));
        }
    }

    private static async Task<TelemetryEvent> ReadTelemetryEventAsync()
    {
        await Task.Delay(1);
        return new TelemetryEvent(101, 4096);
    }

    private static int SerializeTelemetry(TelemetryEvent telemetryEvent, Span<byte> destination)
    {
        BitConverter.TryWriteBytes(destination[..4], telemetryEvent.MetricId);
        BitConverter.TryWriteBytes(destination[4..8], telemetryEvent.Value);
        return 8;
    }

    private static void Send(ReadOnlySpan<byte> payload)
    {
        Console.WriteLine(Convert.ToHexString(payload));
    }
}

internal readonly record struct TelemetryEvent(int MetricId, int Value);

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
