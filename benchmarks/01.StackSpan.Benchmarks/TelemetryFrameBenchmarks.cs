using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class TelemetryFrameBenchmarks
{
    private const int HeaderSize = 4;
    private const int MetricId = 42;
    private const int Value = 1_250;
    private const int TimestampDeltaMs = 37;

    [Benchmark(Baseline = true)]
    public int Heap()
    {
        byte[] frame = new byte[16];

        frame[0] = 1;
        frame[1] = 0b_0000_0011;

        Span<byte> payload = frame.AsSpan(HeaderSize);
        BitConverter.TryWriteBytes(frame.AsSpan(2, 2), checked((ushort)payload.Length));

        BitConverter.TryWriteBytes(payload[..4], MetricId);
        BitConverter.TryWriteBytes(payload.Slice(4, 4), Value);
        BitConverter.TryWriteBytes(payload[8..12], TimestampDeltaMs);

        return TelemetrySinkConsume(frame);
    }

    [Benchmark]
    public int Stackalloc()
    {
        Span<byte> frame = stackalloc byte[16];

        frame[0] = 1;
        frame[1] = 0b_0000_0011;

        Span<byte> payload = frame.Slice(HeaderSize);
        BitConverter.TryWriteBytes(frame.Slice(2, 2), checked((ushort)payload.Length));

        BitConverter.TryWriteBytes(payload[..4], MetricId);
        BitConverter.TryWriteBytes(payload.Slice(4, 4), Value);
        BitConverter.TryWriteBytes(payload[8..12], TimestampDeltaMs);

        return TelemetrySinkConsume(frame);
    }

    private static int TelemetrySinkConsume(ReadOnlySpan<byte> frame)
    {
        var checksum = 17;

        foreach (var b in frame)
        {
            checksum = (checksum * 31) + b;
        }

        return checksum;
    }
}
