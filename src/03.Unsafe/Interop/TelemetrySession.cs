using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Csharp14.SystemsMemory.UnsafeModule.Telemetry;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal sealed class TelemetrySession : IDisposable
{
    private readonly SessionHandle _handle;
    private readonly GCHandle _selfHandle;
    private TaskCompletionSource<NativeCompletion>? _pendingCompletion;

    public TelemetrySession()
    {
        NativeMethods.ThrowOnError(NativeMethods.SessionOpen(out nint session), "ct_session_open");
        _handle = new SessionHandle(session);
        _selfHandle = GCHandle.Alloc(this);

        unsafe
        {
            NativeMethods.ThrowOnError(
                NativeMethods.SessionRegisterCallback(_handle.DangerousGetHandle(), &OnNativeCompletion, GCHandle.ToIntPtr(_selfHandle)),
                "ct_session_register_callback");
        }
    }

    public void Dispose()
    {
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _handle.Dispose();
    }

    public Task<NativeCompletion> SubmitCopyAsync(byte[] frame)
    {
        TaskCompletionSource<NativeCompletion> tcs = ArmSinglePendingOperation();

        unsafe
        {
            fixed (byte* pinned = frame)
            {
                NativeMethods.ThrowOnError(
                    NativeMethods.SessionSubmitCopy(_handle.DangerousGetHandle(), pinned, (uint)frame.Length),
                    "ct_session_submit_copy");
            }
        }

        return tcs.Task;
    }

    public unsafe Task<NativeCompletion> SubmitFastAsync(uint sequence, string tag)
    {
        TaskCompletionSource<NativeCompletion> tcs = ArmSinglePendingOperation();
        OutboundFrameLease lease = OutboundFrameLease.Create(this, sequence, tag);

        try
        {
            FrameCodec.DescribeFrame(lease.GetFrameSpan());

            NativeMethods.ThrowOnError(
                NativeMethods.SessionSubmitZeroCopy(_handle.DangerousGetHandle(), (void*)lease.AllocationPointer, (uint)lease.FrameLength),
                "ct_session_submit_zero_copy");

            return tcs.Task;
        }
        finally
        {
            lease.Dispose();
        }
    }

    public NativeBuffer AllocateBuffer(int capacity)
    {
        NativeMethods.ThrowOnError(NativeMethods.BufferAlloc((uint)capacity, out nint buffer), "ct_buffer_alloc");
        return new NativeBuffer(buffer);
    }

    public void Flush(uint timeoutMs = 5_000)
    {
        NativeMethods.ThrowOnError(NativeMethods.SessionFlush(_handle.DangerousGetHandle(), timeoutMs), "ct_session_flush");
    }

    private TaskCompletionSource<NativeCompletion> ArmSinglePendingOperation()
    {
        if (_pendingCompletion is not null)
        {
            throw new InvalidOperationException("Compact demo supports only one in-flight submission.");
        }

        _pendingCompletion = new TaskCompletionSource<NativeCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pendingCompletion;
    }

    private void Complete(NativeCompletion completion)
    {
        TaskCompletionSource<NativeCompletion>? pending = _pendingCompletion;
        _pendingCompletion = null;
        pending?.TrySetResult(completion);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnNativeCompletion(nint context, nint completionPtr)
    {
        try
        {
            TelemetrySession session = (TelemetrySession)GCHandle.FromIntPtr(context).Target!;
            NativeCompletion completion = Unsafe.AsRef<NativeCompletion>((void*)completionPtr);
            session.Complete(completion);
        }
        catch
        {
            Environment.FailFast("Native completion callback failed.");
        }
    }
}
