using System.Buffers.Binary;
using Csharp14.SystemsMemory.UnsafeModule.Telemetry;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal sealed unsafe class TelemetryFrameTransport : IDisposable
{
    private const uint MetadataStartMarker = 0x314D4454u;
    private const int MetadataStartMarkerSize = sizeof(uint);
    private const int MetadataFrameLengthSize = sizeof(uint);
    private const int MetadataRecordSize = MetadataStartMarkerSize + MetadataFrameLengthSize;

    private readonly HeapBuffer _buffer;

    private TelemetryFrameTransport(HeapBuffer buffer, int frameLength)
    {
        _buffer = buffer;
        FrameLength = frameLength;
    }

    public nint FrameAddress => _buffer.DataPointer;

    public int FrameLength { get; }

    public int AllocationSize => _buffer.AllocationSize;

    public static TelemetryFrameTransport Create(uint sequence, string tag)
    {
        HeapBuffer buffer = HeapBuffer.Allocate(FrameCodec.RecommendedBufferSize);

        try
        {
            int frameLength = FrameCodec.WriteFrameFast((void*)buffer.DataPointer, buffer.Capacity, sequence, tag);
            return new TelemetryFrameTransport(buffer, frameLength);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    public ReadOnlySpan<byte> GetFrameSpan() =>
        new((void*)_buffer.DataPointer, FrameLength);

    public void CommitTransportMetadata()
    {
        Span<byte> metadata = GetTransportMetadataSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(metadata, MetadataStartMarker);
        BinaryPrimitives.WriteUInt32LittleEndian(metadata[MetadataStartMarkerSize..], checked((uint)FrameLength));
    }

    public void Dispose() => _buffer.Dispose();

    private Span<byte> GetTransportMetadataSpan()
    {
        int metadataOffset = AlignUp(FrameLength, MetadataRecordSize);
        return new Span<byte>((byte*)_buffer.DataPointer + metadataOffset, MetadataRecordSize);
    }

    private static int AlignUp(int value, int alignment)
    {
        return checked((value + alignment - 1) & ~(alignment - 1));
    }
}
