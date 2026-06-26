using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace EsoAddons.Services;

public record UpdateInfo(string Version, bool IsNewer, string ReleaseUrl, string ExeUrl, string ZipUrl, string Notes);

/// <summary>Checks GitHub Releases for a newer version of the app.</summary>
public class UpdateChecker
{
    public const string Owner = "shoyru-ai";
    public const string Repo = "eso-addon-manager";
    private static readonly string LatestUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private static readonly string AllReleasesUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=30";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>PPE channel: also consider pre-releases (the highest version wins). PROD: latest stable only.</summary>
    public bool IncludePrereleases { get; }

    public UpdateChecker(bool includePrereleases = false)
    {
        IncludePrereleases = includePrereleases;
        _http.DefaultRequestHeaders.Add("User-Agent", "ESO-Addons-Updater");
        _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    /// <summary>This app's version, e.g. "0.1.0".</summary>
    public static string CurrentVersion =>
        (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>Returns update info, or null on any failure (never throws).</summary>
    public async Task<UpdateInfo?> CheckAsync(string? currentVersion = null)
    {
        try
        {
            var cur = currentVersion ?? CurrentVersion;
            // Always pull the list so we can aggregate notes across every version we're behind.
            return ParseReleasesList(await _http.GetStringAsync(AllReleasesUrl), cur, IncludePrereleases);
        }
        catch { return null; }
    }

    /// <summary>Parses a GitHub /releases/latest payload (a single release object). (Static = testable.)</summary>
    public static UpdateInfo? ParseLatestRelease(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseRelease(doc.RootElement, currentVersion);
    }

    /// <summary>Parses a GitHub /releases LIST. Returns the HIGHEST applicable release as the update target,
    /// with its Notes set to the AGGREGATED notes of every applicable release newer than the current version
    /// (newest first, each under a "vX.Y.Z" header) — so a user several versions behind sees the changelog
    /// for each. Drafts are always skipped; pre-releases are included only when <paramref name="includePrereleases"/>
    /// (the PPE channel). (Static = testable.)</summary>
    public static UpdateInfo? ParseReleasesList(string json, string currentVersion, bool includePrereleases = true)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        UpdateInfo? target = null;
        var newer = new List<UpdateInfo>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            if (!includePrereleases && r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
            var info = ParseRelease(r, currentVersion);
            if (info is null) continue;
            if (target is null || VersionCompare.IsNewer(info.Version, target.Version)) target = info;
            if (info.IsNewer) newer.Add(info);   // strictly newer than the current version
        }
        if (target is null) return null;

        // newest-first
        newer.Sort((a, b) => VersionCompare.IsNewer(a.Version, b.Version) ? -1
                           : VersionCompare.IsNewer(b.Version, a.Version) ? 1 : 0);

        if (newer.Count > 1)
        {
            var sb = new StringBuilder();
            foreach (var i in newer)
            {
                if (sb.Length > 0) sb.Append("\n\n──────────────────\n\n");
                var body = string.IsNullOrWhiteSpace(i.Notes) ? "(no notes)" : i.Notes.Trim();
                sb.Append("v").Append(i.Version).Append("\n\n").Append(body);
            }
            return target with { Notes = sb.ToString() };
        }
        return target;
    }

    /// <summary>Builds UpdateInfo from a single release JSON object.</summary>
    private static UpdateInfo? ParseRelease(JsonElement r, string currentVersion)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;

        var tag = Str(r, "tag_name");
        if (tag.Length == 0) return null;

        string exeUrl = "", zipUrl = "";
        if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = Str(a, "name");
                var url = Str(a, "browser_download_url");
                if (exeUrl.Length == 0 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exeUrl = url;
                if (zipUrl.Length == 0 && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zipUrl = url;
            }
        }

        return new UpdateInfo(
            Version: tag.TrimStart('v', 'V'),
            IsNewer: VersionCompare.IsNewer(tag, currentVersion),
            ReleaseUrl: Str(r, "html_url"),
            ExeUrl: exeUrl,
            ZipUrl: zipUrl,
            Notes: Str(r, "body"));
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
