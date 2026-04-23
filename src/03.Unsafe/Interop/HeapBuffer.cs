using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal sealed class HeapBuffer : SafeHandle
{
    private HeapBuffer()
        : base(nint.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == nint.Zero;

    private nint EnsureOpenHandle()
    {
        if (IsClosed || IsInvalid)
        {
            throw new ObjectDisposedException(nameof(HeapBuffer));
        }

        return handle;
    }

    public nint DataPointer => NativeMethods.HeapBufferData(EnsureOpenHandle());

    public int Capacity => checked((int)NativeMethods.HeapBufferCapacity(EnsureOpenHandle()));

    public int AllocationSize => checked((int)NativeMethods.HeapBufferAllocationSize(EnsureOpenHandle()));

    public static HeapBuffer Allocate(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        NativeMethods.ThrowOnError(
            NativeMethods.HeapBufferAlloc((uint)capacity, out nint handle),
            "hb_alloc");

        return new HeapBuffer { handle = handle };
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.HeapBufferFree(handle);
        handle = nint.Zero;
        return true;
    }

    
}
