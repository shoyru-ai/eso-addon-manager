namespace EsoAddons.Models;

/// <summary>One addon recorded in a snapshot — enough to reinstall it from ESOUI later.</summary>
public class SnapshotAddon
{
    public string FolderName { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>ESOUI file id (empty for custom/unmanaged addons that can't be auto-reinstalled).</summary>
    public string EsouiId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Category { get; set; } = "";
}

/// <summary>The header written into a snapshot bundle (manifest.json). A snapshot captures the
/// installed-addon list plus the user's SavedVariables (in-game addon configs) — see SnapshotService.</summary>
public class SnapshotManifest
{
    public int FormatVersion { get; set; } = 1;
    /// <summary>Display name: a timestamp for backups, a user-chosen name for profiles, "sync" for the sync slot.</summary>
    public string Name { get; set; } = "";
    /// <summary>ISO-8601 UTC creation time (string so it round-trips without timezone surprises).</summary>
    public string CreatedUtc { get; set; } = "";
    public string Machine { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public int SavedVarCount { get; set; }
    public List<SnapshotAddon> Addons { get; set; } = new();
}

/// <summary>A snapshot file on disk plus its parsed manifest — bound directly by the Pro Tools lists.</summary>
public class SnapshotEntry
{
    public string FilePath { get; init; } = "";
    public SnapshotManifest Manifest { get; init; } = new();

    public string Name => string.IsNullOrWhiteSpace(Manifest.Name)
        ? System.IO.Path.GetFileNameWithoutExtension(FilePath)
        : Manifest.Name;

    /// <summary>Local-time creation stamp for display (parsed from the stored UTC string).</summary>
    public string Created =>
        DateTimeOffset.TryParse(Manifest.CreatedUtc, out var dt)
            ? dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "";

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (Created.Length > 0) parts.Add(Created);
            parts.Add($"{Manifest.Addons.Count} addon(s)");
            if (Manifest.SavedVarCount > 0) parts.Add($"{Manifest.SavedVarCount} config(s)");
            if (Manifest.Machine.Length > 0) parts.Add(Manifest.Machine);
            return string.Join("  ·  ", parts);
        }
    }
}
