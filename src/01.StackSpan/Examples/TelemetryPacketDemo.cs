namespace Csharp14.SystemsMemory.StackSpan.Examples;

public static class TelemetryPacketDemo
{
    private const int HeaderSize = 4;
    public static void Run()
    {
        const int metricId = 42;
        const int value = 1_250;
        const int timestampDeltaMs = 37;

        RecordTelemetry(metricId, value, timestampDeltaMs);
    }

    private static void RecordTelemetry(int metricId, int value, int timestampDeltaMs)
    {
        Span<byte> frame = stackalloc byte[16];

        // Fixed layout:
        // [0]     version
        // [1]     flags
        // [2..4)  payload length
        // [4..8)  metric id
        // [8..12) scaled value
        // [12..16) timestamp delta in milliseconds
        frame[0] = 1;
        frame[1] = 0b_0000_0011;

        Span<byte> payload = frame.Slice(HeaderSize);
        BitConverter.TryWriteBytes(frame.Slice(2, 2), checked((ushort)payload.Length));

        Span<byte> metricIdBytes = payload[..4];
        Span<byte> valueBytes = payload.Slice(4, 4);
        Span<byte> timestampBytes = payload[8..12];

        BitConverter.TryWriteBytes(metricIdBytes, metricId);
        BitConverter.TryWriteBytes(valueBytes, value);
        BitConverter.TryWriteBytes(timestampBytes, timestampDeltaMs);

        TelemetrySinkWrite(frame);
    }

    private static void TelemetrySinkWrite(ReadOnlySpan<byte> frame)
    {
        Console.WriteLine(Convert.ToHexString(frame));
    }
}
