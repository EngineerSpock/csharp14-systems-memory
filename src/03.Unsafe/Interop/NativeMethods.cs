using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal static unsafe partial class NativeMethods
{
    public const string LibraryName = "CompactTelemetryNative";

    [LibraryImport(LibraryName, EntryPoint = "ct_session_open")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionOpen(uint demoMode, out nint session);

    [LibraryImport(LibraryName, EntryPoint = "ct_session_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SessionClose(nint session);

    [LibraryImport(LibraryName, EntryPoint = "ct_session_register_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionRegisterCallback(nint session, delegate* unmanaged[Cdecl]<nint, nint, void> callback, nint context);

    [LibraryImport(LibraryName, EntryPoint = "ct_session_submit_copy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionSubmitCopy(nint session, void* frame, uint frameLength);

    [LibraryImport(LibraryName, EntryPoint = "ct_session_submit_zero_copy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionSubmitZeroCopy(nint session, void* frame, uint frameLength);

    [LibraryImport(LibraryName, EntryPoint = "ct_session_flush")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionFlush(nint session, uint timeoutMs);

    [LibraryImport(LibraryName, EntryPoint = "ct_buffer_alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int BufferAlloc(uint capacity, out nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "ct_buffer_data")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint BufferData(nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "ct_buffer_capacity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint BufferCapacity(nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "ct_buffer_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void BufferFree(nint buffer);

    internal static void ThrowOnError(int status, string operation)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"{operation} failed with status {status}.");
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCompletion
{
    public NativeCompletionStatus Status;
    public uint SubmittedLength;
    public uint CopiedLength;
    public uint ObservedChecksum;
    public ulong FrameAddress;

    public override string ToString()
    {
        return $"""
Completion:
  status           : {Status}
  submitted length : {SubmittedLength}
  copied length    : {CopiedLength}
  checksum         : 0x{ObservedChecksum:X8}
  frame address    : 0x{FrameAddress:X}
""";
    }
}
