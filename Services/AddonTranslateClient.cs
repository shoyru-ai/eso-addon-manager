using System.Net.Http;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>Calls the backend <c>/translate</c> endpoint — machine-translates a single add-on
/// description/changelog (ESOUI author-written content we can't curate) into the user's UI language, on
/// demand. The DeepL key lives only on the backend; this client sends the text + target language + the
/// caller's Pro license key (validated server-side before any quota is spent). Never throws.</summary>
public class AddonTranslateClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static string Base => BusinessConfig.Current.BackendBaseUrl.TrimEnd('/');
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(BusinessConfig.Current.BackendBaseUrl);

    /// <summary>Translated text, or an optional user-facing error (e.g. daily-cap/quota message). Both null = no-op.</summary>
    public record TranslateResult(string? Text, string? Error);

    public async Task<TranslateResult> TranslateAsync(string text, string target, string licenseKey)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(licenseKey))
            return new TranslateResult(null, null);
        try
        {
            var json = JsonSerializer.Serialize(new { text, target, licenseKey });
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{Base}/translate", content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Diag.Log($"translate POST {(int)resp.StatusCode}: {body}");
                return new TranslateResult(null, ExtractError(body) ?? $"Translation failed ({(int)resp.StatusCode}).");
            }
            using var doc = JsonDocument.Parse(body);
            var t = doc.RootElement.TryGetProperty("text", out var x) ? x.GetString() : null;
            return new TranslateResult(t, null);
        }
        catch (Exception ex) { Diag.Log("translate POST ex: " + ex.Message); return new TranslateResult(null, null); }
    }

    private static string? ExtractError(string body)
    {
        try { using var d = JsonDocument.Parse(body); return d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null; }
        catch { return null; }
    }
}
