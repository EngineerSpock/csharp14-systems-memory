using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab.Telemetry;

internal static class FrameCodec
{
    public const int SampleCount = 3;
    public static readonly int RecommendedBufferSize = TelemetryWireHeader.Size + (SampleCount * Unsafe.SizeOf<TelemetrySample>());

    public static byte[] BuildManagedFrame(uint sequence, string tag)
    {
        Span<TelemetrySample> samples = stackalloc TelemetrySample[SampleCount];
        FillSamples(samples, sequence);

        ReadOnlySpan<byte> payload = MemoryMarshal.AsBytes(samples);
        TelemetryWireHeader header = CreateHeader(sequence, tag, payload);

        byte[] frame = GC.AllocateUninitializedArray<byte>(TelemetryWireHeader.Size + payload.Length);
        Span<byte> destination = frame;

        WriteUnmanaged(destination, header);
        payload.CopyTo(destination[TelemetryWireHeader.Size..]);

        return frame;
    }

    public static unsafe int WriteFrameFast(void* destination, nuint capacity, uint sequence, string tag)
    {
        Span<TelemetrySample> samples = stackalloc TelemetrySample[SampleCount];
        FillSamples(samples, sequence);

        ReadOnlySpan<byte> payload = MemoryMarshal.AsBytes(samples);
        TelemetryWireHeader header = CreateHeader(sequence, tag, payload);

        nuint totalLength = (nuint)(TelemetryWireHeader.Size + payload.Length);
        if (capacity < totalLength)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), $"Need at least {totalLength} bytes.");
        }

        ref byte destinationRef = ref Unsafe.AsRef<byte>(destination);
        Unsafe.WriteUnaligned(ref destinationRef, header);

        // Payload length is expressed in bytes, so byte-based stepping is the correct model.
        ref byte payloadRef = ref Unsafe.AddByteOffset(ref destinationRef, (nint)TelemetryWireHeader.Size);

        for (nuint i = 0; i < (nuint)samples.Length; i++)
        {
            nuint offset = i * (nuint)Unsafe.SizeOf<TelemetrySample>();
            Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref payloadRef, checked((nint)offset)), samples[(int)i]);
        }

        return checked((int)totalLength);
    }

    public static void DescribeManagedFrame(ReadOnlySpan<byte> frame)
    {
        TelemetryWireHeader header = Unsafe.ReadUnaligned<TelemetryWireHeader>(ref MemoryMarshal.GetReference(frame));
        uint checksum = ComputeManagedChecksum(frame[TelemetryWireHeader.Size..]);

        Console.WriteLine($"Managed inspect: header={header.HeaderSize}, payload={header.PayloadLength}, sequence={header.Sequence}, tag={header.DecodeTag()}, checksum=0x{header.PayloadChecksum:X8}, recomputed=0x{checksum:X8}");
    }

    private static void FillSamples(Span<TelemetrySample> samples, uint sequence)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = new TelemetrySample
            {
                SensorId = 500 + i,
                MilliUnits = checked((int)(sequence * 10u) + (i * 75)),
                TimestampDeltaUs = 250L * (i + 1)
            };
        }
    }

    private static TelemetryWireHeader CreateHeader(uint sequence, string tag, ReadOnlySpan<byte> payload)
    {
        TelemetryWireHeader header = new()
        {
            Magic = TelemetryWireHeader.MagicValue,
            HeaderSize = TelemetryWireHeader.Size,
            Opcode = 0x1201,
            PayloadLength = checked((uint)payload.Length),
            Sequence = sequence,
            Flags = 0x0003,
            Reserved = 0,
            CorrelationId = 0xABCDEF0000000000ul | sequence
        };

        header.SetTag(System.Text.Encoding.ASCII.GetBytes(tag));
        header.PayloadChecksum = ComputeManagedChecksum(payload);
        return header;
    }

    private static uint ComputeManagedChecksum(ReadOnlySpan<byte> payload)
    {
        uint checksum = 2166136261u;

        for (int i = 0; i < payload.Length; i++)
        {
            checksum ^= payload[i];
            checksum *= 16777619u;
            checksum = BitOperations.RotateLeft(checksum, 5) ^ (uint)(i + 1);
        }

        return checksum;
    }

    private static void WriteUnmanaged<T>(Span<byte> destination, in T value)
        where T : unmanaged
    {
        if (destination.Length < Unsafe.SizeOf<T>())
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
    }
}
