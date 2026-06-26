using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using EsoAddons.Models;

namespace EsoAddons.Services;

/// <summary>Talks to the public ESOUI / mmoui v3 API for the ESO addon catalog.</summary>
public partial class EsouiClient
{
    public const string LibraryCategoryId = "53";
    private const string FileListUrl = "https://api.mmoui.com/v3/game/ESO/filelist.json";
    private const string FileDetailsUrl = "https://api.mmoui.com/v3/game/ESO/filedetails/{0}.json";
    private const string CategoryListUrl = "https://api.mmoui.com/v3/game/ESO/categorylist.json";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private List<EsouiAddon>? _catalog;

    public EsouiClient() => _http.DefaultRequestHeaders.Add("User-Agent", "ESO-Addons/0.1");

    /// <summary>Full catalog (~3000 entries). Cached after first fetch.</summary>
    public async Task<IReadOnlyList<EsouiAddon>> GetCatalogAsync(bool refresh = false)
    {
        if (_catalog is not null && !refresh) return _catalog;
        _catalog = ParseCatalog(await _http.GetStringAsync(FileListUrl));
        return _catalog;
    }

    /// <summary>Parses a filelist.json payload into catalog entries. (Static = unit-testable.)</summary>
    public static List<EsouiAddon> ParseCatalog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<EsouiAddon>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            list.Add(new EsouiAddon
            {
                Id = Str(e, "UID"),
                Title = StripColor(Str(e, "UIName")),
                Author = StripColor(Str(e, "UIAuthorName")),
                Version = StripColor(Str(e, "UIVersion")),
                Downloads = Num(e, "UIDownloadTotal"),
                FileInfoUri = Str(e, "UIFileInfoURL"),
                LastUpdateMs = Num(e, "UIDate"),
                CategoryId = Str(e, "UICATID"),
                Dirs = Dirs(e, "UIDir"),
                ThumbUrl = FirstImage(e, "UIIMG_Thumbs"),
                IsLibrary = Str(e, "UICATID") == LibraryCategoryId,
            });
        }
        return list;
    }

    public record AddonDetails(string DownloadUrl, string Version, List<string> Dirs,
                               string Description, string ChangeLog, string ImageUrl);

    /// <summary>Per-addon details: download URL, dirs, description, changelog, preview image.</summary>
    public async Task<AddonDetails> GetDetailsAsync(string id)
        => ParseDetails(await _http.GetStringAsync(string.Format(FileDetailsUrl, id)), id);

    /// <summary>Parses a filedetails.json payload. (Static = unit-testable.)</summary>
    public static AddonDetails ParseDetails(string json, string id = "")
    {
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement.EnumerateArray().FirstOrDefault();
        if (e.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"No details for id {id}.");

        var url = Str(e, "UIDownload");
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException($"No download URL for id {id}.");

        return new AddonDetails(
            url,
            StripColor(Str(e, "UIVersion")),
            Dirs(e, "UIDir"),
            BBCode.ToText(Str(e, "UIDescription")),
            BBCode.ToText(Str(e, "UIChangeLog")),
            FirstImage(e, "UIIMGs"));
    }

    /// <summary>Fetches the category list (buckets).</summary>
    public async Task<List<Category>> GetCategoriesAsync()
        => ParseCategories(await _http.GetStringAsync(CategoryListUrl));

    /// <summary>Parses categorylist.json. (Static = unit-testable.)</summary>
    public static List<Category> ParseCategories(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Category>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var title = StripColor(Str(e, "UICATTitle"));
            if (string.IsNullOrWhiteSpace(title)) continue;
            list.Add(new Category { Id = Str(e, "UICATID"), Title = title, Count = (int)Num(e, "UICATFileCount") });
        }
        return list;
    }

    private static string FirstImage(JsonElement e, string name)
    {
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s) return s;
        return "";
    }

    public async Task<byte[]> DownloadAsync(string url) => await _http.GetByteArrayAsync(url);

    // ---- helpers ----
    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Number => v.ToString(), _ => "" }
            : "";

    private static long Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return 0;
    }

    private static List<string> Dirs(JsonElement e, string name)
    {
        var dirs = new List<string>();
        if (!e.TryGetProperty(name, out var v)) return dirs;
        if (v.ValueKind == JsonValueKind.Array)
            dirs.AddRange(v.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0));
        else if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
            dirs.Add(s);
        return dirs;
    }

    [GeneratedRegex(@"\|c[0-9A-Fa-f]{6}|\|r")]
    private static partial Regex ColorCode();

    /// <summary>Removes ESO UI color codes like |cFF0000 ... |r.</summary>
    public static string StripColor(string s) => string.IsNullOrEmpty(s) ? "" : ColorCode().Replace(s, "").Trim();
}
