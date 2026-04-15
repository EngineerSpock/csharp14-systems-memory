using Csharp14.SystemsMemory.MemoryCorruptionLab;
using Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;
using Csharp14.SystemsMemory.MemoryCorruptionLab.Telemetry;

var options = DemoOptions.Parse(args);

Console.WriteLine("Memory Corruption Lab");
Console.WriteLine($"Mode: {options.Mode}");
Console.WriteLine();

using var session = new TelemetrySession(options.Mode, options.WorkerDelayMs);

switch (options.Mode)
{
    case DemoMode.Healthy:
    case DemoMode.BugA:
        await RunCopyPathAsync(session, options);
        break;

    case DemoMode.BugB:
        await RunBugBAsync(session, options);
        break;

    case DemoMode.Fixed:
        await RunFixedZeroCopyAsync(session, options);
        break;

    default:
        throw new InvalidOperationException($"Unsupported mode '{options.Mode}'.");
}

return;

static async Task RunCopyPathAsync(TelemetrySession session, DemoOptions options)
{
    byte[] frame = FrameCodec.BuildManagedFrame(options.Sequence, options.Tag);
    FrameCodec.DescribeManagedFrame(frame);
    session.DescribeNativeInspection(frame);

    NativeCompletion completion = await session.SubmitCopyAsync(frame, options.Token);
    Console.WriteLine();
    Console.WriteLine(CompletionFormatter.Format(completion));
    session.Flush(options.FlushTimeoutMs);
}

static async Task RunBugBAsync(TelemetrySession session, DemoOptions options)
{
    using NativePacketBufferHandle buffer = session.AllocatePacketBuffer(FrameCodec.RecommendedBufferSize);
    Task<NativeCompletion> completionTask;

    unsafe
    {
        nint framePointer = session.GetBufferPointer(buffer);
        int written = FrameCodec.WriteFrameFast((void*)framePointer, (nuint)session.GetBufferCapacity(buffer), options.Sequence, options.Tag);
        session.DescribeNativeInspection((void*)framePointer, written);

        completionTask = session.SubmitZeroCopyAsync((void*)framePointer, written, options.Token);

        // This is the bug: the session still holds only the raw pointer, but the caller
        // destroys the owning buffer immediately after enqueue.
        buffer.Dispose();
        HeapNoise.StressNativeBuffers(session);
    }

    NativeCompletion completion = await completionTask;
    Console.WriteLine();
    Console.WriteLine(CompletionFormatter.Format(completion));
}

static async Task RunFixedZeroCopyAsync(TelemetrySession session, DemoOptions options)
{
    NativePacketBufferHandle buffer = session.AllocatePacketBuffer(FrameCodec.RecommendedBufferSize);
    Task<NativeCompletion> completionTask;

    unsafe
    {
        nint framePointer = session.GetBufferPointer(buffer);
        int written = FrameCodec.WriteFrameFast((void*)framePointer, (nuint)session.GetBufferCapacity(buffer), options.Sequence, options.Tag);
        session.DescribeNativeInspection((void*)framePointer, written);

        completionTask = session.SubmitZeroCopyAsync((void*)framePointer, written, options.Token, buffer);
    }

    NativeCompletion completion = await completionTask;
    Console.WriteLine();
    Console.WriteLine(CompletionFormatter.Format(completion));
    session.Flush(options.FlushTimeoutMs);
}
