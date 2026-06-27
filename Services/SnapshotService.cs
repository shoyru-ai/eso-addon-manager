using System.IO;
using System.IO.Compression;
using System.Text.Json;
using EsoAddons.Models;

namespace EsoAddons.Services;

/// <summary>
/// Packs/unpacks "snapshots" — a portable bundle of the user's installed-addon list (manifest.json)
/// plus their SavedVariables (the in-game addon configs, the precious hard-to-recreate part).
/// One bundle backs all three Pro features: backups (timestamped, local), profiles (named, local),
/// and multi-PC sync (the same bundle written to a cloud-synced folder).
/// </summary>
public static class SnapshotService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string BackupsDir => Path.Combine(StateRoot, "backups");
    public static string ProfilesDir => Path.Combine(StateRoot, "profiles");

    private static string StateRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ESO Addons");

    /// <summary>The SavedVariables folder is the sibling of AddOns (…\live\SavedVariables).</summary>
    public static string SavedVarsDirFor(string addonsPath) =>
        Path.GetFullPath(Path.Combine(addonsPath, "..", "SavedVariables"));

    /// <summary>ESO's enabled/disabled addon list lives next to AddOns (…\live\AddOnSettings.txt).
    /// Capturing it lets a profile restore *which addons are active*, not just their settings.</summary>
    public static string AddOnSettingsPathFor(string addonsPath) =>
        Path.GetFullPath(Path.Combine(addonsPath, "..", "AddOnSettings.txt"));

    private const string AddOnSettingsEntry = "AddOnSettings.txt";

    /// <summary>File name for the single shared sync slot inside the user's sync folder.</summary>
    public const string SyncFileName = "shoyru-addon-suite-sync.snapshot.zip";

    /// <summary>Builds a snapshot zip in memory: manifest.json + every SavedVariables\*.lua + (if present)
    /// AddOnSettings.txt. Stamps SavedVarCount/HasAddOnSettings onto the manifest so list rows can show
    /// them without opening the zip.</summary>
    public static byte[] BuildZip(SnapshotManifest manifest, string savedVarsDir, string? addOnSettingsPath = null)
    {
        var luaFiles = Directory.Exists(savedVarsDir)
            ? Directory.GetFiles(savedVarsDir, "*.lua")
            : Array.Empty<string>();
        manifest.SavedVarCount = luaFiles.Length;
        manifest.HasAddOnSettings = addOnSettingsPath is not null && File.Exists(addOnSettingsPath);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("manifest.json");
            using (var w = new StreamWriter(entry.Open()))
                w.Write(JsonSerializer.Serialize(manifest, JsonOpts));

            foreach (var f in luaFiles)
                zip.CreateEntryFromFile(f, "SavedVariables/" + Path.GetFileName(f));

            if (manifest.HasAddOnSettings)
                zip.CreateEntryFromFile(addOnSettingsPath!, AddOnSettingsEntry);
        }
        return ms.ToArray();
    }

    /// <summary>Builds a snapshot and writes it to <paramref name="destZipPath"/> (creating dirs).</summary>
    public static void Write(SnapshotManifest manifest, string savedVarsDir, string destZipPath, string? addOnSettingsPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destZipPath)!);
        File.WriteAllBytes(destZipPath, BuildZip(manifest, savedVarsDir, addOnSettingsPath));
    }

    /// <summary>Reads just the manifest header from a snapshot zip (null if missing/unreadable).</summary>
    public static SnapshotManifest? ReadManifest(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry("manifest.json");
            if (entry is null) return null;
            using var r = new StreamReader(entry.Open());
            return JsonSerializer.Deserialize<SnapshotManifest>(r.ReadToEnd());
        }
        catch { return null; }
    }

    /// <summary>Lists snapshot bundles in a directory, newest first, skipping any that won't parse.</summary>
    public static List<SnapshotEntry> List(string dir)
    {
        var result = new List<SnapshotEntry>();
        if (!Directory.Exists(dir)) return result;
        foreach (var f in Directory.GetFiles(dir, "*.zip"))
        {
            var m = ReadManifest(f);
            if (m is not null) result.Add(new SnapshotEntry { FilePath = f, Manifest = m });
        }
        return result
            .OrderByDescending(e => e.Manifest.CreatedUtc, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Restores the SavedVariables\*.lua files from a snapshot into <paramref name="savedVarsDir"/>
    /// (overwriting), plus AddOnSettings.txt to <paramref name="addOnSettingsPath"/> when both the snapshot
    /// contains it and a path is given. Returns how many SavedVariables files were written. Guards zip-slip.</summary>
    public static int RestoreSavedVars(string zipPath, string savedVarsDir, string? addOnSettingsPath = null)
    {
        Directory.CreateDirectory(savedVarsDir);
        var root = Path.GetFullPath(savedVarsDir);
        int n = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var e in archive.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue;                                  // directory entry

            if (e.FullName.StartsWith("SavedVariables/", StringComparison.OrdinalIgnoreCase))
            {
                var dest = Path.GetFullPath(Path.Combine(savedVarsDir, e.Name));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                e.ExtractToFile(dest, overwrite: true);
                n++;
            }
            else if (addOnSettingsPath is not null &&
                     e.FullName.Equals(AddOnSettingsEntry, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(addOnSettingsPath)!);
                e.ExtractToFile(addOnSettingsPath, overwrite: true);
            }
        }
        return n;
    }

    /// <summary>A filesystem-safe file name for a user-chosen profile name.</summary>
    public static string SafeFileName(string name)
    {
        var cleaned = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return cleaned.Length == 0 ? "profile" : cleaned;
    }
}
