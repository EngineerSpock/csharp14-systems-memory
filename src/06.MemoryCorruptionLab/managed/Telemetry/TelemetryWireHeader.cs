using System.Runtime.InteropServices;
using System.Text;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab.Telemetry;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct TelemetryWireHeader
{
    public const uint MagicValue = 0x314D4C54;
    public const int Size = 40;
    public const int ProcessedFlag = 0x8000;

    public uint Magic;
    public ushort HeaderSize;
    public ushort Opcode;
    public uint PayloadLength;
    public uint Sequence;
    public ushort Flags;
    public ushort Reserved;
    public ulong CorrelationId;
    public fixed byte Tag[8];
    public uint PayloadChecksum;

    public void SetTag(ReadOnlySpan<byte> source)
    {
        fixed (byte* tag = Tag)
        {
            Span<byte> destination = new(tag, 8);
            destination.Clear();
            source[..Math.Min(source.Length, destination.Length)].CopyTo(destination);
        }
    }

    public readonly string DecodeTag()
    {
        fixed (byte* tag = Tag)
        {
            return Encoding.ASCII.GetString(new ReadOnlySpan<byte>(tag, 8)).TrimEnd('\0', ' ');
        }
    }
}
