using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;

internal static unsafe partial class NativeMethods
{
    public const string LibraryName = "TelemetryNative";

    [LibraryImport(LibraryName, EntryPoint = "tm_session_open")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionOpen(ref NativeSessionOptions options, out nint session);

    [LibraryImport(LibraryName, EntryPoint = "tm_session_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SessionClose(nint session);

    [LibraryImport(LibraryName, EntryPoint = "tm_session_register_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionRegisterCallback(nint session, delegate* unmanaged[Cdecl]<nint, nint, void> callback, nint context);

    [LibraryImport(LibraryName, EntryPoint = "tm_session_submit_copy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int SessionSubmitCopy(nint session, void* frame, uint frameLength, ulong token);

    [LibraryImport(LibraryName, EntryPoint = "tm_session_submit_zero_copy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int SessionSubmitZeroCopy(nint session, void* frame, uint frameLength, ulong token);

    [LibraryImport(LibraryName, EntryPoint = "tm_session_allocate_packet_buffer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int AllocatePacketBuffer(nint session, uint capacity, out nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "tm_packet_buffer_data")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint PacketBufferData(nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "tm_packet_buffer_capacity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint PacketBufferCapacity(nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "tm_packet_buffer_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void PacketBufferRelease(nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "tm_compute_checksum")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial uint ComputeChecksum(void* data, uint length);

    [LibraryImport(LibraryName, EntryPoint = "tm_inspect_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int InspectFrame(void* frame, uint frameLength, out NativePacketInspection inspection);

    [LibraryImport(LibraryName, EntryPoint = "tm_session_flush")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SessionFlush(nint session, uint timeoutMs);

    internal static void ThrowOnError(int errorCode, string operation)
    {
        if (errorCode != 0)
        {
            throw new InvalidOperationException($"{operation} failed with status {errorCode}.");
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSessionOptions
{
    public uint StructSize;
    public uint DemoMode;
    public uint WorkerDelayMs;
    public uint DebugFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCompletion
{
    public ulong Token;
    public uint Status;
    public uint SubmittedLength;
    public uint ObservedChecksum;
    public uint CopiedLength;
    public uint Reserved;
    public ulong FrameAddress;
    public ulong Note1;
    public ulong Note2;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePacketInspection
{
    public uint Status;
    public uint HeaderSize;
    public uint PayloadLength;
    public uint PayloadChecksum;
    public uint Sequence;
    public ulong CorrelationId;
    public fixed byte Tag[8];

    public override string ToString()
    {
        fixed (byte* tag = Tag)
        {
            string decodedTag = System.Text.Encoding.ASCII.GetString(new ReadOnlySpan<byte>(tag, 8)).TrimEnd('\0', ' ');
            return $"status={Status}, header={HeaderSize}, payload={PayloadLength}, checksum=0x{PayloadChecksum:X8}, sequence={Sequence}, correlation=0x{CorrelationId:X}, tag={decodedTag}";
        }
    }
}
