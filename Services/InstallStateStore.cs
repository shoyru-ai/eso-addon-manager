using System.IO;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>
/// Persists the ESOUI version that was installed for each addon folder, so update detection
/// compares like-for-like (recorded ESOUI version vs current ESOUI version) instead of the
/// unreliable manifest-vs-ESOUI comparison. Stored as JSON in LocalAppData.
/// </summary>
public class InstallStateStore
{
    private readonly string _path;
    private Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public InstallStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ESO Addons", "state.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
                _map = loaded is null
                    ? new(StringComparer.OrdinalIgnoreCase)
                    : new(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { _map = new(StringComparer.OrdinalIgnoreCase); }
    }

    public string? Get(string folder) => _map.TryGetValue(folder, out var v) ? v : null;

    public void Set(string folder, string version) => _map[folder] = version;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
