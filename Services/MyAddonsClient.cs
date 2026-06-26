using System.Net.Http;
using System.Text.Json;
using EsoAddons.Models;

namespace EsoAddons.Services;

/// <summary>Fetches Shoyru's published-addons manifest from GitHub.</summary>
public class MyAddonsClient
{
    public const string ManifestUrl =
        "https://raw.githubusercontent.com/shoyru-ai/shoyru-eso-addons/main/manifest.json";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public MyAddonsClient() => _http.DefaultRequestHeaders.Add("User-Agent", "ESO-Addons");

    public async Task<List<PublishedAddon>> GetAsync()
    {
        try { return ParseManifest(await _http.GetStringAsync(ManifestUrl)); }
        catch { return new List<PublishedAddon>(); }
    }

    /// <summary>Parses the published-addons manifest.json. (Static = unit-testable.)</summary>
    public static List<PublishedAddon> ParseManifest(string json)
    {
        var list = new List<PublishedAddon>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("addons", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                var name = Str(e, "name");
                if (name.Length == 0) continue;
                list.Add(new PublishedAddon
                {
                    Name = name,
                    Title = Str(e, "title") is { Length: > 0 } t ? t : name,
                    Version = Str(e, "version"),
                    Description = Str(e, "description"),
                    DownloadUrl = Str(e, "downloadUrl"),
                });
            }
        }
        return list;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
