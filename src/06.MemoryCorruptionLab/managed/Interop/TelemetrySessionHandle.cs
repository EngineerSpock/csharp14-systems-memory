using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;

internal sealed class TelemetrySessionHandle : SafeHandle
{
    public TelemetrySessionHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    internal TelemetrySessionHandle(nint handle)
        : this()
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.SessionClose(handle);
        return true;
    }
}
