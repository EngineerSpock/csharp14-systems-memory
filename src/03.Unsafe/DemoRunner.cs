using Csharp14.SystemsMemory.UnsafeModule.Interop;
using Csharp14.SystemsMemory.UnsafeModule.Telemetry;

namespace Csharp14.SystemsMemory.UnsafeModule;

internal static class DemoRunner
{
    private const uint DemoSequence = 42;
    private const string DemoTag = "OPSDEMO";

    public static void Run(string[] args)
    {
        PrintHeader();
        RunHeapDemo();
    }

    private static void RunHeapDemo()
    {
        using TelemetryFrameTransport transport = TelemetryFrameTransport.Create(DemoSequence, DemoTag);

        FrameCodec.DescribeFrame(transport.GetFrameSpan());
        Console.WriteLine();
        PrintHeapBlock(transport);

        Console.WriteLine();
        Console.WriteLine("Committing transport metadata...");
        transport.CommitTransportMetadata();

        Console.WriteLine();
        Console.WriteLine("Metadata commit returned. The corrupted heap block will be released now.");
    }

    private static void PrintHeader()
    {
        Console.WriteLine("Module 03: unsafe");
        Console.WriteLine("Scenario: unmanaged heap corruption");
        Console.WriteLine();
    }

    private static void PrintHeapBlock(TelemetryFrameTransport transport)
    {
        Console.WriteLine("Heap buffer");
        Console.WriteLine($"  frame address   : 0x{transport.FrameAddress:X}");
        Console.WriteLine($"  frame length    : {transport.FrameLength}");
        Console.WriteLine($"  allocation size : {transport.AllocationSize}");
    }
}
