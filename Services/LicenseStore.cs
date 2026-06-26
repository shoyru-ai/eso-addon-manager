using System.IO;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>The locally-saved Pro license (key + this device's Lemon Squeezy instance).</summary>
public class LicenseInfo
{
    public string Key { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public DateTime LastValidatedUtc { get; set; }
    /// <summary>Last known Pro state — used as an offline grace fallback when the API is unreachable.</summary>
    public bool ProCached { get; set; }

    public bool HasKey => Key.Length > 0;
}

/// <summary>Persists the Pro license at %LOCALAPPDATA%\ESO Addons\license.json.</summary>
public class LicenseStore
{
    private static readonly string Path_ =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ESO Addons", "license.json");

    public LicenseInfo Load()
    {
        try
        {
            if (File.Exists(Path_))
                return JsonSerializer.Deserialize<LicenseInfo>(File.ReadAllText(Path_)) ?? new LicenseInfo();
        }
        catch { /* ignore corrupt/missing */ }
        return new LicenseInfo();
    }

    public void Save(LicenseInfo info)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }

    public void Clear()
    {
        try { if (File.Exists(Path_)) File.Delete(Path_); } catch { /* ignore */ }
    }
}
