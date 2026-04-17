using Csharp14.SystemsMemory.UnsafeModule.Interop;
using Csharp14.SystemsMemory.UnsafeModule.Telemetry;

namespace Csharp14.SystemsMemory.UnsafeModule.Examples;

internal static class CompactInteropCorruptionDemo
{
    private const uint DemoSequence = 42;
    private const string DemoTag = "OPSDEMO";

    public static async Task RunAsync(string[] args)
    {
        DemoOptions options = DemoOptions.Parse(args);
        DemoMode mode = options.Mode;

        PrintHeader(mode);

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
        byte[] frame = FrameCodec.BuildManagedFrame(DemoSequence, DemoTag);
        FrameCodec.DescribeFrame(frame);

        NativeCompletion completion = await session.SubmitCopyAsync(frame);
        Console.WriteLine();
        Console.WriteLine(completion);
        session.Flush();
    }

    private static async Task RunZeroCopyPathAsync(TelemetrySession session, bool retainOwner)
    {
        using NativeBuffer buffer = session.AllocateBuffer(FrameCodec.RecommendedBufferSize);
        Task<NativeCompletion> completionTask;

        unsafe
        {
            int written = FrameCodec.WriteFrameFast((void*)buffer.Pointer, (nuint)buffer.Capacity, DemoSequence, DemoTag);
            FrameCodec.DescribeFrame(buffer.AsReadOnlySpan(written));
            completionTask = session.SubmitZeroCopyAsync(buffer, written, retainOwner);
        }

        if (!retainOwner)
        {
            buffer.Dispose();
        }

        NativeCompletion completion = await completionTask;
        Console.WriteLine();
        Console.WriteLine(completion);
        session.Flush();
    }

    private static void PrintHeader(DemoMode mode)
    {
        Console.WriteLine("Module 03: unsafe");
        Console.WriteLine("Compact telemetry interop demo");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine();
    }
}
