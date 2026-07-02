using System.Net.Http;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>Calls the backend <c>/find</c> endpoint: turns a natural-language description into a ranked list
/// of ESOUI add-on ids + one-line reasons. The Anthropic key lives only on the backend; this client just
/// sends the description plus the caller's Pro license key (which the backend validates before spending any
/// tokens). No-op (empty result, never throws) when no backend is configured.</summary>
public class AddonFinderClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static string Base => BusinessConfig.Current.BackendBaseUrl.TrimEnd('/');
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(BusinessConfig.Current.BackendBaseUrl);

    public record Match(string Id, string Reason);
    /// <summary>Matches plus an optional user-facing error (e.g. the daily-cap message on HTTP 429).</summary>
    public record FindResult(IReadOnlyList<Match> Matches, string? Error);

    private static readonly FindResult Empty = new(Array.Empty<Match>(), null);

    /// <summary>Returns ranked matches (id + why-it-fits). On a handled failure (e.g. daily cap), Matches is
    /// empty and Error carries a message to show; never throws.</summary>
    public async Task<FindResult> FindAsync(string query, string licenseKey)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(licenseKey))
            return Empty;
        try
        {
            var json = JsonSerializer.Serialize(new { query, licenseKey });
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{Base}/find", content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Diag.Log($"find POST {(int)resp.StatusCode}: {body}");
                var msg = ExtractError(body)
                    ?? (resp.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "AI Describe isn’t available yet — the finder service isn’t set up."
                        : $"The finder returned an error ({(int)resp.StatusCode}).");
                return new FindResult(Array.Empty<Match>(), msg);
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("matches", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return Empty;
            var list = new List<Match>();
            foreach (var m in arr.EnumerateArray())
            {
                var id = m.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                var reason = m.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                if (id.Length > 0) list.Add(new Match(id, reason));
            }
            return new FindResult(list, null);
        }
        catch (Exception ex) { Diag.Log("find POST ex: " + ex.Message); return Empty; }
    }

    private static string? ExtractError(string body)
    {
        try { using var d = JsonDocument.Parse(body); return d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null; }
        catch { return null; }
    }
}
