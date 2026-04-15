using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;

internal sealed class CompletionRouter : IDisposable
{
    private readonly GCHandle _selfHandle;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<NativeCompletion>> _pending = new();
    private readonly ConcurrentDictionary<ulong, NativePacketBufferHandle> _retainedBuffers = new();

    public CompletionRouter()
    {
        _selfHandle = GCHandle.Alloc(this);
    }

    public nint Context => GCHandle.ToIntPtr(_selfHandle);

    public static unsafe delegate* unmanaged[Cdecl]<nint, nint, void> Callback => &OnCompletion;

    public Task<NativeCompletion> Track(ulong token, NativePacketBufferHandle? retainedBuffer = null)
    {
        var tcs = new TaskCompletionSource<NativeCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(token, tcs))
        {
            throw new InvalidOperationException($"Token 0x{token:X} is already pending.");
        }

        if (retainedBuffer is not null && !_retainedBuffers.TryAdd(token, retainedBuffer))
        {
            _pending.TryRemove(token, out _);
            throw new InvalidOperationException($"Token 0x{token:X} already has a retained buffer.");
        }

        return tcs.Task;
    }

    public void Dispose()
    {
        foreach ((ulong _, NativePacketBufferHandle handle) in _retainedBuffers)
        {
            handle.Dispose();
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void Complete(in NativeCompletion completion)
    {
        if (_retainedBuffers.TryRemove(completion.Token, out NativePacketBufferHandle? retained))
        {
            retained.Dispose();
        }

        if (_pending.TryRemove(completion.Token, out TaskCompletionSource<NativeCompletion>? pending))
        {
            pending.TrySetResult(completion);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnCompletion(nint context, nint completionPtr)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(context);
            var router = (CompletionRouter)handle.Target!;
            NativeCompletion completion = Unsafe.AsRef<NativeCompletion>((void*)completionPtr);
            router.Complete(completion);
        }
        catch
        {
            Environment.FailFast("Managed completion callback threw.");
        }
    }
}
