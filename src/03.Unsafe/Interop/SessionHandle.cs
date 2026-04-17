using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal sealed class SessionHandle : SafeHandle
{
    public SessionHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public SessionHandle(nint handle)
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
