using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;

internal sealed class NativePacketBufferHandle : SafeHandle
{
    public NativePacketBufferHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    internal NativePacketBufferHandle(nint handle)
        : this()
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.PacketBufferRelease(handle);
        return true;
    }
}
