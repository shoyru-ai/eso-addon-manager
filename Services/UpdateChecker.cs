using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace EsoAddons.Services;

public record UpdateInfo(string Version, bool IsNewer, string ReleaseUrl, string ExeUrl, string ZipUrl, string Notes);

/// <summary>Checks GitHub Releases for a newer version of the app.</summary>
public class UpdateChecker
{
    public const string Owner = "shoyru-ai";
    public const string Repo = "eso-addon-manager";
    private static readonly string LatestUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public UpdateChecker()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "ESO-Addons-Updater");
        _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    /// <summary>This app's version, e.g. "0.1.0".</summary>
    public static string CurrentVersion =>
        (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>Returns update info, or null on any failure (never throws).</summary>
    public async Task<UpdateInfo?> CheckAsync(string? currentVersion = null)
    {
        try { return ParseLatestRelease(await _http.GetStringAsync(LatestUrl), currentVersion ?? CurrentVersion); }
        catch { return null; }
    }

    /// <summary>Parses a GitHub /releases/latest payload. (Static = unit-testable.)</summary>
    public static UpdateInfo? ParseLatestRelease(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
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
