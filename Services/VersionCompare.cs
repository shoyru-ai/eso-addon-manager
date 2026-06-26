using System.Text.RegularExpressions;

namespace EsoAddons.Services;

/// <summary>Tolerant version comparison for ESO addon version strings (e.g. "2.0 r43", "v5", "1.3.7").</summary>
public static partial class VersionCompare
{
    /// <summary>True if <paramref name="latest"/> is numerically newer than <paramref name="installed"/>.
    /// Extracts numeric groups and compares them component-wise, so "1.0" == "1.0.0" and
    /// "3.0.42" > "3.0". If either side has no parseable numbers, returns false (don't guess).</summary>
    public static bool IsNewer(string? latest, string? installed)
    {
        var a = Parts(latest);
        var b = Parts(installed);
        if (a.Count == 0 || b.Count == 0) return false;

        int n = Math.Max(a.Count, b.Count);
        for (int i = 0; i < n; i++)
        {
            int x = i < a.Count ? a[i] : 0;
            int y = i < b.Count ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private static List<int> Parts(string? s)
    {
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (Match m in Digits().Matches(s))
            if (int.TryParse(m.Value, out var v)) list.Add(v);
        return list;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
