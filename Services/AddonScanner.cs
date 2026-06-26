using System.IO;
using EsoAddons.Models;

namespace EsoAddons.Services;

/// <summary>Reads the live AddOns folder and parses each addon's .txt manifest.</summary>
public static class AddonScanner
{
    /// <summary>Default ESO AddOns path: Documents\Elder Scrolls Online\live\AddOns.</summary>
    public static string DefaultAddonsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Elder Scrolls Online", "live", "AddOns");

    public static List<InstalledAddon> Scan(string addonsPath)
    {
        var result = new List<InstalledAddon>();
        if (!Directory.Exists(addonsPath)) return result;

        foreach (var dir in Directory.GetDirectories(addonsPath))
        {
            var folder = Path.GetFileName(dir);
            var manifest = FindManifest(dir, folder);
            if (manifest is null) continue;   // not an addon root

            var meta = ParseManifest(manifest);
            result.Add(new InstalledAddon
            {
                FolderName = folder,
                Title = string.IsNullOrWhiteSpace(meta.Title) ? folder : meta.Title,
                Author = meta.Author,
                Version = meta.Version,
                Description = meta.Description,
                IsLibrary = meta.IsLibrary || folder.StartsWith("Lib", StringComparison.OrdinalIgnoreCase),
                Dependencies = meta.DependsOn,
                OptionalDependencies = meta.OptionalDependsOn,
                Path = dir,
            });
        }
        return result.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ESO addon manifests can use either extension.
    private static readonly string[] ManifestExtensions = { ".txt", ".addon" };

    private static string? FindManifest(string dir, string folder)
    {
        foreach (var ext in ManifestExtensions)
        {
            var direct = Path.Combine(dir, folder + ext);
            if (File.Exists(direct)) return direct;
        }
        foreach (var manifest in ManifestExtensions.SelectMany(ext => Directory.GetFiles(dir, "*" + ext)))
        {
            try
            {
                if (File.ReadLines(manifest).Take(40).Any(l => l.TrimStart().StartsWith("## Title", StringComparison.OrdinalIgnoreCase)))
                    return manifest;
            }
            catch { /* unreadable file – skip */ }
        }
        return null;
    }

    private record Meta(string Title, string Author, string Version, string Description, bool IsLibrary,
                        List<string> DependsOn, List<string> OptionalDependsOn);

    private static Meta ParseManifest(string file)
    {
        string title = "", author = "", version = "", desc = "";
        bool isLib = false;
        var deps = new List<string>();
        var optDeps = new List<string>();
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (!line.StartsWith("##")) continue;
            var body = line[2..].TrimStart();
            var idx = body.IndexOf(':');
            if (idx <= 0) continue;
            var key = body[..idx].Trim();
            var val = EsouiClient.StripColor(body[(idx + 1)..].Trim());
            switch (key.ToLowerInvariant())
            {
                case "title": title = val; break;
                case "author": author = val; break;
                case "version": version = val; break;
                case "description": desc = val; break;
                case "islibrary": isLib = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "dependson": deps.AddRange(ParseDeps(val)); break;
                case "optionaldependson": optDeps.AddRange(ParseDeps(val)); break;
            }
        }
        return new Meta(title, author, version, desc, isLib, deps, optDeps);
    }

    /// <summary>Splits a DependsOn value into addon names, stripping version constraints (e.g. "LibStub&gt;=20").</summary>
    private static IEnumerable<string> ParseDeps(string value) =>
        value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(t => t.Split('>', '<', '=')[0].Trim().Trim('"'))
             .Where(t => t.Length > 0);
}
