namespace EsoAddons.Services;

/// <summary>Tiny command-line option reader (static = unit-testable).</summary>
public static class CliArgs
{
    /// <summary>Reads "--name value" or "--name=value" from args (case-insensitive). Null if absent/empty.</summary>
    public static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return string.IsNullOrWhiteSpace(args[i + 1]) ? null : args[i + 1];

            var prefix = name + "=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var v = args[i][prefix.Length..];
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }
        return null;
    }
}
