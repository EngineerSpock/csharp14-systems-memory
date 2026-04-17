namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal sealed class NativeBuffer : IDisposable
{
    private nint _handle;

    public NativeBuffer(nint handle)
    {
        _handle = handle;
    }

    public nint Pointer
    {
        get
        {
            EnsureNotDisposed();
            return NativeMethods.BufferData(_handle);
        }
    }

    public int Capacity
    {
        get
        {
            EnsureNotDisposed();
            return checked((int)NativeMethods.BufferCapacity(_handle));
        }
    }

    public unsafe Span<byte> AsSpan(int length)
    {
        EnsureUsableLength(length);
        return new Span<byte>((void*)NativeMethods.BufferData(_handle), length);
    }

    public unsafe ReadOnlySpan<byte> AsReadOnlySpan(int length)
    {
        EnsureUsableLength(length);
        return new ReadOnlySpan<byte>((void*)NativeMethods.BufferData(_handle), length);
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        NativeMethods.BufferFree(_handle);
        _handle = 0;
    }

    private void EnsureNotDisposed()
    {
        if (_handle == 0)
        {
            throw new ObjectDisposedException(nameof(NativeBuffer));
        }
    }

    private void EnsureUsableLength(int length)
    {
        EnsureNotDisposed();

        if ((uint)length > (uint)Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Length must be between 0 and {Capacity}.");
        }
    }
}
