using System.Buffers.Binary;
using Csharp14.SystemsMemory.UnsafeModule.Telemetry;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal sealed class OutboundFrameLease : IDisposable
{
    private const int TransportPrefixSize = 16;

    private readonly NativeBuffer _buffer;
    private readonly int _frameOffset;

    private OutboundFrameLease(NativeBuffer buffer, int frameOffset, int frameLength)
    {
        _buffer = buffer;
        _frameOffset = frameOffset;
        FrameLength = frameLength;
    }

    public nint AllocationPointer => _buffer.Pointer;

    public nint FramePointer => AllocationPointer + _frameOffset;

    public int FrameLength { get; }

    public static unsafe OutboundFrameLease Create(TelemetrySession session, uint sequence, string tag)
    {
        NativeBuffer buffer = session.AllocateBuffer(TransportPrefixSize + FrameCodec.RecommendedBufferSize);
        StampTransportPrefix(buffer.AsSpan(TransportPrefixSize));

        int frameLength = FrameCodec.WriteFrameFast(
            (void*)(buffer.Pointer + TransportPrefixSize),
            buffer.Capacity - TransportPrefixSize,
            sequence,
            tag);

        return new OutboundFrameLease(buffer, TransportPrefixSize, frameLength);
    }

    public ReadOnlySpan<byte> GetFrameSpan() =>
        _buffer.AsReadOnlySpan(_frameOffset + FrameLength).Slice(_frameOffset, FrameLength);

    public void Dispose() => _buffer.Dispose();

    private static void StampTransportPrefix(Span<byte> prefix)
    {
        prefix.Fill(0xA5);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, 0x31584650u);
    }
}
