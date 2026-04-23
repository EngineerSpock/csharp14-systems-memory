using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal static unsafe partial class NativeMethods
{
    public const string LibraryName = "HeapBufferNative";

    [LibraryImport(LibraryName, EntryPoint = "hb_alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int HeapBufferAlloc(uint frameCapacity, out nint handle);

    [LibraryImport(LibraryName, EntryPoint = "hb_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void HeapBufferFree(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "hb_frame_data")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint HeapBufferData(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "hb_frame_capacity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint HeapBufferCapacity(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "hb_allocation_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint HeapBufferAllocationSize(nint handle);

    internal static void ThrowOnError(int status, string operation)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"{operation} failed with status {status}.");
        }
    }
}
