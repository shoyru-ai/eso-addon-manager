using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>Client for the Shoyru subscription-management backend (Azure Function). The backend holds the
/// Lemon Squeezy API key; we authenticate by sending our own license key, which it validates server-side.
/// Disabled (returns null) when no BackendBaseUrl is configured.</summary>
public class SubscriptionBackend
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static string Base => BusinessConfig.Current.BackendBaseUrl.TrimEnd('/');
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(BusinessConfig.Current.BackendBaseUrl);

    /// <summary>Returns the customer's signed Lemon Squeezy portal URL, or null on failure.</summary>
    public async Task<string?> GetPortalUrlAsync(string licenseKey)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{Base}/portal", new { licenseKey });
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("portalUrl", out var u) ? u.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>Cancels the caller's subscription (at period end). Returns (ok, endsAt, error).</summary>
    public async Task<(bool ok, string endsAt, string error)> CancelAsync(string licenseKey)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{Base}/cancel", new { licenseKey });
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!resp.IsSuccessStatusCode)
                return (false, "", root.TryGetProperty("error", out var e) ? e.GetString() ?? "Cancel failed." : "Cancel failed.");
            var endsAt = root.TryGetProperty("endsAt", out var d) ? d.GetString() ?? "" : "";
            return (true, endsAt, "");
        }
        catch (Exception ex) { return (false, "", ex.Message); }
    }
}
