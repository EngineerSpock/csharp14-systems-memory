using Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab;

internal static class HeapNoise
{
    public static void StressNativeBuffers(TelemetrySession session)
    {
        for (int i = 0; i < 32; i++)
        {
            using NativePacketBufferHandle noise = session.AllocatePacketBuffer(256);
            unsafe
            {
                new Span<byte>((void*)session.GetBufferPointer(noise), 256).Fill((byte)(0xA0 + i));
            }
        }
    }
}
