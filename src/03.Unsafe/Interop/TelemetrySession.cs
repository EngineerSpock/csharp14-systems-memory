using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal sealed class TelemetrySession : IDisposable
{
    private readonly SessionHandle _handle;
    private readonly GCHandle _selfHandle;
    private TaskCompletionSource<NativeCompletion>? _pendingCompletion;
    private NativeBuffer? _retainedBuffer;

    public TelemetrySession(NativeDemoMode mode)
    {
        NativeMethods.ThrowOnError(NativeMethods.SessionOpen((uint)mode, out nint session), "ct_session_open");
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
        _retainedBuffer?.Dispose();

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

    public unsafe Task<NativeCompletion> SubmitZeroCopyAsync(NativeBuffer buffer, int frameLength, bool retainOwner)
    {
        TaskCompletionSource<NativeCompletion> tcs = ArmSinglePendingOperation();

        if (retainOwner)
        {
            _retainedBuffer = buffer;
        }

        NativeMethods.ThrowOnError(
            NativeMethods.SessionSubmitZeroCopy(_handle.DangerousGetHandle(), (void*)buffer.Pointer, (uint)frameLength),
            "ct_session_submit_zero_copy");

        return tcs.Task;
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
        _retainedBuffer?.Dispose();
        _retainedBuffer = null;

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
