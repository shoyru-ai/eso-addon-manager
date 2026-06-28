using System.Net.Http;
using System.Net.Http.Json;

namespace EsoAddons.Services;

/// <summary>Sends user feedback to the backend (/feedback), auto-attaching app version, tier, and OS so we
/// don't have to ask. No-op when no backend is configured.</summary>
public class FeedbackClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static string Base => BusinessConfig.Current.BackendBaseUrl.TrimEnd('/');
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(BusinessConfig.Current.BackendBaseUrl);

    public async Task<bool> SendAsync(string type, string message, string contact, bool isPro)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(message)) return false;
        try
        {
            var payload = new
            {
                type,
                message,
                contact,
                appVersion = UpdateChecker.CurrentVersion,
                tier = isPro ? "Pro" : "Free",
                os = Environment.OSVersion.VersionString,
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{Base}/feedback", content);
            if (!resp.IsSuccessStatusCode)
                Diag.Log($"feedback POST {(int)resp.StatusCode} : {await resp.Content.ReadAsStringAsync()}");
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { Diag.Log("feedback POST ex: " + ex.Message + " (base='" + Base + "')"); return false; }
    }
}
