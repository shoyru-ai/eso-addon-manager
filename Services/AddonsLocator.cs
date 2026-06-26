using System.IO;

namespace EsoAddons.Services;

/// <summary>
/// Locates the live ESO AddOns folder across the common locations (standard Documents,
/// OneDrive-redirected Documents, explicit user override). Designed so the picking logic
/// is pure/testable.
/// </summary>
public static class AddonsLocator
{
    private const string SubPath = @"Elder Scrolls Online\live\AddOns";

    /// <summary>Candidate AddOns paths in priority order (standard, then OneDrive variants).</summary>
    public static List<string> DefaultCandidates()
    {
        var bases = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
            Combine(Environment.GetEnvironmentVariable("OneDrive"), "Documents"),
            Combine(Environment.GetEnvironmentVariable("OneDriveConsumer"), "Documents"),
            Combine(Environment.GetEnvironmentVariable("OneDriveCommercial"), "Documents"),
        };

        return bases
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => Path.Combine(b!, SubPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        static string? Combine(string? a, string b) => string.IsNullOrWhiteSpace(a) ? null : Path.Combine(a!, b);
    }

    /// <summary>Returns the first candidate that exists; if none exist, the first candidate (for display) and found=false.</summary>
    public static (string path, bool found) Pick(IReadOnlyList<string> candidates, Func<string, bool> exists)
    {
        foreach (var c in candidates)
            if (exists(c)) return (c, true);
        return (candidates.Count > 0 ? candidates[0] : "", false);
    }

    /// <summary>Resolves the AddOns path: an existing override wins, else the first existing default, else the first default.</summary>
    public static (string path, bool found) Resolve(string? overridePath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(overridePath!);
        candidates.AddRange(DefaultCandidates());
        return Pick(candidates, Directory.Exists);
    }
}
