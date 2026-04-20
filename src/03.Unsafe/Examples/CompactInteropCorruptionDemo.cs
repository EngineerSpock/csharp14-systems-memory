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

        using TelemetrySession session = new();

        switch (mode)
        {
            case DemoMode.Copy:
                await RunCopyPathAsync(session);
                break;
            case DemoMode.Fast:
                await RunFastPathAsync(session);
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

    private static async Task RunFastPathAsync(TelemetrySession session)
    {
        NativeCompletion completion = await session.SubmitFastAsync(DemoSequence, DemoTag);
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
