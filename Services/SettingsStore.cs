using System.IO;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>User-configurable settings, persisted as JSON in LocalAppData.</summary>
public class AppSettings
{
    /// <summary>If set, overrides auto-detection of the AddOns folder.</summary>
    public string AddonsPathOverride { get; set; } = "";
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
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
