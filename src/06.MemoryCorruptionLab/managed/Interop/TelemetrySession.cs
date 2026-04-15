using System.Runtime.CompilerServices;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;

internal sealed class TelemetrySession : IDisposable
{
    private readonly TelemetrySessionHandle _handle;
    private readonly CompletionRouter _router;

    public TelemetrySession(DemoMode mode, uint workerDelayMs)
    {
        NativeSessionOptions options = new()
        {
            StructSize = (uint)Unsafe.SizeOf<NativeSessionOptions>(),
            DemoMode = mode == DemoMode.BugA ? 1u : 0u,
            WorkerDelayMs = workerDelayMs,
            DebugFlags = 0
        };

        NativeMethods.ThrowOnError(NativeMethods.SessionOpen(ref options, out nint sessionHandle), "tm_session_open");
        _handle = new TelemetrySessionHandle(sessionHandle);
        _router = new CompletionRouter();

        unsafe
        {
            NativeMethods.ThrowOnError(
                NativeMethods.SessionRegisterCallback(_handle.DangerousGetHandle(), CompletionRouter.Callback, _router.Context),
                "tm_session_register_callback");
        }
    }

    public void Dispose()
    {
        _router.Dispose();
        _handle.Dispose();
    }

    public Task<NativeCompletion> SubmitCopyAsync(byte[] frame, ulong token)
    {
        Task<NativeCompletion> completionTask = _router.Track(token);

        unsafe
        {
            fixed (byte* pinnedFrame = frame)
            {
                NativeMethods.ThrowOnError(
                    NativeMethods.SessionSubmitCopy(_handle.DangerousGetHandle(), pinnedFrame, (uint)frame.Length, token),
                    "tm_session_submit_copy");
            }
        }

        return completionTask;
    }

    public unsafe Task<NativeCompletion> SubmitZeroCopyAsync(void* frame, int frameLength, ulong token, NativePacketBufferHandle? retainedBuffer = null)
    {
        Task<NativeCompletion> completionTask = _router.Track(token, retainedBuffer);

        NativeMethods.ThrowOnError(
            NativeMethods.SessionSubmitZeroCopy(_handle.DangerousGetHandle(), frame, (uint)frameLength, token),
            "tm_session_submit_zero_copy");

        return completionTask;
    }

    public NativePacketBufferHandle AllocatePacketBuffer(int capacity)
    {
        NativeMethods.ThrowOnError(
            NativeMethods.AllocatePacketBuffer(_handle.DangerousGetHandle(), (uint)capacity, out nint bufferHandle),
            "tm_session_allocate_packet_buffer");

        return new NativePacketBufferHandle(bufferHandle);
    }

    public nint GetBufferPointer(NativePacketBufferHandle buffer) => NativeMethods.PacketBufferData(buffer.DangerousGetHandle());

    public int GetBufferCapacity(NativePacketBufferHandle buffer) => checked((int)NativeMethods.PacketBufferCapacity(buffer.DangerousGetHandle()));

    public void Flush(uint timeoutMs)
    {
        NativeMethods.ThrowOnError(
            NativeMethods.SessionFlush(_handle.DangerousGetHandle(), timeoutMs),
            "tm_session_flush");
    }

    public void DescribeNativeInspection(byte[] frame)
    {
        unsafe
        {
            fixed (byte* pinnedFrame = frame)
            {
                DescribeNativeInspection(pinnedFrame, frame.Length);
            }
        }
    }

    public unsafe void DescribeNativeInspection(void* frame, int frameLength)
    {
        NativeMethods.ThrowOnError(
            NativeMethods.InspectFrame(frame, (uint)frameLength, out NativePacketInspection inspection),
            "tm_inspect_frame");

        uint checksum = NativeMethods.ComputeChecksum((byte*)frame + Telemetry.TelemetryWireHeader.Size, inspection.PayloadLength);
        Console.WriteLine($"Native inspect: {inspection}");
        Console.WriteLine($"Native checksum over payload: 0x{checksum:X8}");
    }
}
