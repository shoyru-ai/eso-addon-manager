using System.IO;
using System.IO.Compression;
using EsoAddons.Models;

namespace EsoAddons.Services;

/// <summary>Downloads addon zips from ESOUI and extracts them into the AddOns folder; handles removal.</summary>
public class AddonInstaller
{
    private readonly EsouiClient _client;
    public AddonInstaller(EsouiClient client) => _client = client;

    /// <summary>Downloads the given ESOUI id and extracts it into <paramref name="addonsPath"/> (overwriting).</summary>
    public async Task<List<string>> InstallAsync(string esouiId, string addonsPath)
    {
        var info = await _client.GetDetailsAsync(esouiId);
        var bytes = await _client.DownloadAsync(info.DownloadUrl);
        ExtractZip(bytes, addonsPath);
        return info.Dirs;
    }

    /// <summary>Extracts a zip's entries into the AddOns folder (overwrite), guarding against zip-slip.
    /// Returns the top-level folder names that were written. (Static = unit-testable.)</summary>
    public static List<string> ExtractZip(byte[] zipBytes, string addonsPath)
    {
        Directory.CreateDirectory(addonsPath);
        var root = Path.GetFullPath(addonsPath);
        var topDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var destPath = Path.GetFullPath(Path.Combine(addonsPath, entry.FullName));
            if (!destPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);

            var rel = Path.GetRelativePath(root, destPath);
            var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (!string.IsNullOrEmpty(first)) topDirs.Add(first);
        }
        return topDirs.ToList();
    }

    /// <summary>Removes an installed addon's folder. Refuses to touch junctions (your custom/repo addons).</summary>
    public static (bool ok, string message) Uninstall(InstalledAddon addon)
    {
        if (!Directory.Exists(addon.Path))
            return (false, "Folder no longer exists.");

        var attr = File.GetAttributes(addon.Path);
        if (attr.HasFlag(FileAttributes.ReparsePoint))
            return (false, $"'{addon.FolderName}' is a junction (a custom/linked addon). Not removing it to protect your source.");

        try
        {
            Directory.Delete(addon.Path, recursive: true);
            return (true, $"Removed {addon.Title}.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not remove {addon.FolderName}: {ex.Message}");
        }
    }
}
