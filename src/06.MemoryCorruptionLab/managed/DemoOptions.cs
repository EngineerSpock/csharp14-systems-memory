namespace Csharp14.SystemsMemory.MemoryCorruptionLab;

internal enum DemoMode
{
    Healthy,
    BugA,
    BugB,
    Fixed
}

internal sealed record DemoOptions(DemoMode Mode, uint WorkerDelayMs, uint Sequence, string Tag, ulong Token, uint FlushTimeoutMs)
{
    public static DemoOptions Parse(string[] args)
    {
        DemoMode mode = DemoMode.Healthy;
        uint workerDelayMs = 150;
        uint sequence = 42;
        string tag = "OPSDEMO";
        ulong token = 0x1001;
        uint flushTimeoutMs = 5_000;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    mode = ParseMode(ReadValue(args, ref i));
                    break;
                case "--delay":
                    workerDelayMs = uint.Parse(ReadValue(args, ref i));
                    break;
                case "--sequence":
                    sequence = uint.Parse(ReadValue(args, ref i));
                    break;
                case "--tag":
                    tag = ReadValue(args, ref i);
                    break;
                case "--token":
                    token = ulong.Parse(ReadValue(args, ref i));
                    break;
                case "--flush-timeout":
                    flushTimeoutMs = uint.Parse(ReadValue(args, ref i));
                    break;
                case "--help":
                case "-h":
                    PrintHelpAndExit();
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new DemoOptions(mode, workerDelayMs, sequence, tag, token, flushTimeoutMs);
    }

    private static DemoMode ParseMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "healthy" => DemoMode.Healthy,
            "buga" => DemoMode.BugA,
            "bugb" => DemoMode.BugB,
            "fixed" => DemoMode.Fixed,
            _ => throw new ArgumentException($"Unknown mode '{value}'.")
        };

    private static string ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value after '{args[index]}'.");
        }

        index++;
        return args[index];
    }

    private static void PrintHelpAndExit()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/06.MemoryCorruptionLab/managed -- --mode healthy|bugA|bugB|fixed [--delay 150] [--sequence 42] [--tag OPSDEMO]");
        Environment.Exit(0);
    }
}
