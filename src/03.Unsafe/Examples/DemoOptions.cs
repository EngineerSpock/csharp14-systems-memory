namespace Csharp14.SystemsMemory.UnsafeModule.Examples;

internal readonly record struct DemoOptions(DemoMode Mode)
{
    public static DemoOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new(DemoMode.Copy);
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
            "copy" => DemoMode.Copy,
            "fast" => DemoMode.Fast,
            _ => throw new ArgumentException($"Unknown mode '{value}'.")
        };
}
