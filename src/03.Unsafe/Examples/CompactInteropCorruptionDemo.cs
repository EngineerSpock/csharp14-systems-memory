using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Csharp14.SystemsMemory.UnsafeModule.Interop;

namespace Csharp14.SystemsMemory.UnsafeModule.Examples;

internal static class CompactInteropCorruptionDemo
{
    public static async Task RunAsync(string[] args)
    {
        DemoMode mode = ParseMode(args);

        Console.WriteLine("Module 03: unsafe");
        Console.WriteLine("Compact telemetry interop demo");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine();

        using TelemetrySession session = new(mode == DemoMode.BugA ? NativeDemoMode.LayoutOverflow : NativeDemoMode.Healthy);

        switch (mode)
        {
            case DemoMode.Healthy:
            case DemoMode.BugA:
                await RunCopyPathAsync(session);
                break;
            case DemoMode.BugB:
                await RunZeroCopyPathAsync(session, retainOwner: false);
                break;
            case DemoMode.Fixed:
                await RunZeroCopyPathAsync(session, retainOwner: true);
                break;
        }
    }

    private static async Task RunCopyPathAsync(TelemetrySession session)
    {
        byte[] frame = FrameCodec.BuildManagedFrame(42, "OPSDEMO");
        FrameCodec.DescribeFrame(frame);

        NativeCompletion completion = await session.SubmitCopyAsync(frame);
        Console.WriteLine();
        Console.WriteLine(Format(completion));
        session.Flush();
    }

    private static async Task RunZeroCopyPathAsync(TelemetrySession session, bool retainOwner)
    {
        using NativeBuffer buffer = session.AllocateBuffer(FrameCodec.RecommendedBufferSize);
        Task<NativeCompletion> completionTask;

        unsafe
        {
            int written = FrameCodec.WriteFrameFast((void*)buffer.Pointer, (nuint)buffer.Capacity, 42, "OPSDEMO");
            FrameCodec.DescribeFrame(new ReadOnlySpan<byte>((void*)buffer.Pointer, written));
            completionTask = session.SubmitZeroCopyAsync(buffer, written, retainOwner);
        }

        if (!retainOwner)
        {
            buffer.Dispose();
        }

        NativeCompletion completion = await completionTask;
        Console.WriteLine();
        Console.WriteLine(Format(completion));
        session.Flush();
    }

    private static string Format(NativeCompletion completion)
    {
        return $"""
Completion:
  status           : {completion.Status}
  submitted length : {completion.SubmittedLength}
  copied length    : {completion.CopiedLength}
  checksum         : 0x{completion.ObservedChecksum:X8}
  frame address    : 0x{completion.FrameAddress:X}
""";
    }

    private static DemoMode ParseMode(string[] args)
    {
        if (args.Length == 0)
        {
            return DemoMode.Healthy;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return ParseModeValue(args[i + 1]);
            }
        }

        return ParseModeValue(args[0]);
    }

    private static DemoMode ParseModeValue(string value) =>
        value.ToLowerInvariant() switch
        {
            "healthy" => DemoMode.Healthy,
            "buga" => DemoMode.BugA,
            "bugb" => DemoMode.BugB,
            "fixed" => DemoMode.Fixed,
            _ => throw new ArgumentException($"Unknown mode '{value}'.")
        };

    private enum DemoMode
    {
        Healthy,
        BugA,
        BugB,
        Fixed
    }

    private enum NativeDemoMode : uint
    {
        Healthy = 0,
        LayoutOverflow = 1
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct TelemetryWireHeader
    {
        public const uint MagicValue = 0x314D4C54;
        public const int Size = 40;

        public uint Magic;
        public ushort HeaderSize;
        public ushort Opcode;
        public uint PayloadLength;
        public uint Sequence;
        public ushort Flags;
        public ushort Reserved;
        public ulong CorrelationId;
        public fixed byte Tag[8];
        public uint PayloadChecksum;

        public void SetTag(ReadOnlySpan<byte> source)
        {
            fixed (byte* tag = Tag)
            {
                Span<byte> destination = new(tag, 8);
                destination.Clear();
                source[..Math.Min(source.Length, destination.Length)].CopyTo(destination);
            }
        }

        public readonly string TagAsString()
        {
            fixed (byte* tag = Tag)
            {
                return Encoding.ASCII.GetString(new ReadOnlySpan<byte>(tag, 8)).TrimEnd('\0', ' ');
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TelemetrySample
    {
        public int SensorId;
        public int MilliUnits;
        public long TimestampDeltaUs;
    }

    private static class FrameCodec
    {
        public const int SampleCount = 3;
        public static readonly int RecommendedBufferSize = TelemetryWireHeader.Size + (SampleCount * Unsafe.SizeOf<TelemetrySample>());

        public static byte[] BuildManagedFrame(uint sequence, string tag)
        {
            Span<TelemetrySample> samples = stackalloc TelemetrySample[SampleCount];
            FillSamples(samples, sequence);

            ReadOnlySpan<byte> payload = MemoryMarshal.AsBytes(samples);
            TelemetryWireHeader header = CreateHeader(sequence, tag, payload);

            byte[] frame = GC.AllocateUninitializedArray<byte>(TelemetryWireHeader.Size + payload.Length);
            Span<byte> destination = frame;
            WriteStruct(destination, header);
            payload.CopyTo(destination[TelemetryWireHeader.Size..]);

            return frame;
        }

        public static unsafe int WriteFrameFast(void* destination, nuint capacity, uint sequence, string tag)
        {
            Span<TelemetrySample> samples = stackalloc TelemetrySample[SampleCount];
            FillSamples(samples, sequence);

            ReadOnlySpan<byte> payload = MemoryMarshal.AsBytes(samples);
            TelemetryWireHeader header = CreateHeader(sequence, tag, payload);
            nuint totalLength = (nuint)(TelemetryWireHeader.Size + payload.Length);

            if (capacity < totalLength)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), $"Need at least {totalLength} bytes.");
            }

            ref byte destinationRef = ref Unsafe.AsRef<byte>(destination);
            Unsafe.WriteUnaligned(ref destinationRef, header);

            ref byte payloadRef = ref Unsafe.AddByteOffset(ref destinationRef, (nint)TelemetryWireHeader.Size);
            for (nuint i = 0; i < (nuint)samples.Length; i++)
            {
                nuint offset = i * (nuint)Unsafe.SizeOf<TelemetrySample>();
                Unsafe.WriteUnaligned(ref Unsafe.AddByteOffset(ref payloadRef, checked((nint)offset)), samples[(int)i]);
            }

            return checked((int)totalLength);
        }

        public static void DescribeFrame(ReadOnlySpan<byte> frame)
        {
            TelemetryWireHeader header = Unsafe.ReadUnaligned<TelemetryWireHeader>(ref MemoryMarshal.GetReference(frame));
            uint checksum = ComputeChecksum(frame[TelemetryWireHeader.Size..]);

            Console.WriteLine($"Frame: header={header.HeaderSize}, payload={header.PayloadLength}, sequence={header.Sequence}, tag={header.TagAsString()}, checksum=0x{header.PayloadChecksum:X8}, recomputed=0x{checksum:X8}");
        }

        private static void FillSamples(Span<TelemetrySample> samples, uint sequence)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = new TelemetrySample
                {
                    SensorId = 700 + i,
                    MilliUnits = checked((int)(sequence * 10u) + (i * 25)),
                    TimestampDeltaUs = 125L * (i + 1)
                };
            }
        }

        private static TelemetryWireHeader CreateHeader(uint sequence, string tag, ReadOnlySpan<byte> payload)
        {
            TelemetryWireHeader header = new()
            {
                Magic = TelemetryWireHeader.MagicValue,
                HeaderSize = TelemetryWireHeader.Size,
                Opcode = 0x1201,
                PayloadLength = checked((uint)payload.Length),
                Sequence = sequence,
                Flags = 0x0003,
                Reserved = 0,
                CorrelationId = 0xABCDEF0000000000ul | sequence
            };

            header.SetTag(Encoding.ASCII.GetBytes(tag));
            header.PayloadChecksum = ComputeChecksum(payload);
            return header;
        }

        private static uint ComputeChecksum(ReadOnlySpan<byte> payload)
        {
            uint checksum = 2166136261u;

            for (int i = 0; i < payload.Length; i++)
            {
                checksum ^= payload[i];
                checksum *= 16777619u;
                checksum = BitOperations.RotateLeft(checksum, 5) ^ (uint)(i + 1);
            }

            return checksum;
        }

        private static void WriteStruct<T>(Span<byte> destination, in T value)
            where T : unmanaged
        {
            if (destination.Length < Unsafe.SizeOf<T>())
            {
                throw new ArgumentException("Destination span is too small.", nameof(destination));
            }

            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), value);
        }
    }

    private sealed class TelemetrySession : IDisposable
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

    private sealed class NativeBuffer : IDisposable
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
    }

    private sealed class SessionHandle : SafeHandle
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
}
