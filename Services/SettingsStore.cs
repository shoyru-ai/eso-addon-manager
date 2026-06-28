using System.IO;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>User-configurable settings, persisted as JSON in LocalAppData.</summary>
public class AppSettings
{
    /// <summary>If set, overrides auto-detection of the AddOns folder.</summary>
    public string AddonsPathOverride { get; set; } = "";

    /// <summary>UI theme: "dark" (default) or "light". Switching is a Pro feature.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Pro: when true, the app updates all out-of-date addons automatically on launch.</summary>
    public bool AutoUpdateOnLaunch { get; set; } = false;

    /// <summary>Pro: folder (typically inside OneDrive/Dropbox/Google Drive) where the sync snapshot
    /// is written/read, so profiles + configs follow you between PCs. Empty = sync not configured.</summary>
    public string SyncFolder { get; set; } = "";

    /// <summary>Pro: user-assigned category per installed addon, keyed by folder name. Lets the
    /// Installed tab group addons into custom buckets. Persisted across sessions.</summary>
    public Dictionary<string, string> InstalledCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Highest walkthrough version the user has completed (or skipped). The app auto-shows the
    /// walkthrough while this is below the app's current walkthrough version, so a fresh install (0) sees it,
    /// and bumping the walkthrough version re-greets everyone once. Only advanced when the tour is finished
    /// or skipped — not merely opened — so an interrupted first launch still greets next time.</summary>
    public int WalkthroughVersion { get; set; } = 0;

    /// <summary>The app version last launched, so after an update we can offer a "what's new" tour of
    /// only the features added since. Empty = never recorded.</summary>
    public string LastSeenVersion { get; set; } = "";
}

public class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ESO Addons", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
                // System.Text.Json rebuilds the dictionary with a case-sensitive comparer; restore
                // OrdinalIgnoreCase so folder-name lookups match regardless of case.
                s.InstalledCategories = new Dictionary<string, string>(s.InstalledCategories, StringComparer.OrdinalIgnoreCase);
                return s;
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
