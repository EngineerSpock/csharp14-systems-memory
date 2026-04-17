namespace Csharp14.SystemsMemory.UnsafeModule.Examples;

internal readonly record struct DemoOptions(DemoMode Mode)
{
    public static DemoOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new(DemoMode.Healthy);
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return new(ParseMode(args[i + 1]));
            }
        }

        return new(ParseMode(args[0]));
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
}
