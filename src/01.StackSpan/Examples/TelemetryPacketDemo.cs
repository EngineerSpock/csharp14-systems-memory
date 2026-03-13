namespace Csharp14.SystemsMemory.StackSpan.Examples;

internal static class TelemetryPacketDemo
{
    private const int HeaderSize = 4;

    public static void Run()
    {
        const int metricId = 42;
        const int value = 1_250;
        const int timestampDeltaMs = 37;

        Console.WriteLine("16-byte telemetry frame built on the stack:");
        RecordTelemetry(metricId, value, timestampDeltaMs);
        Console.WriteLine();

        Console.WriteLine("Span and Slice are views over existing memory:");
        ShowViewSemantics();
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

    private static void ShowViewSemantics()
    {
        byte[] buffer = [10, 20, 30, 40];
        Span<byte> span = buffer;
        Span<byte> tail = span.Slice(2);

        tail[0] = 99;

        Console.WriteLine($"buffer[2] = {buffer[2]}");
    }

    private static void TelemetrySinkWrite(ReadOnlySpan<byte> frame)
    {
        Console.WriteLine(Convert.ToHexString(frame));
    }
}
