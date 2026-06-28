using System.Net.Http;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>Result of a Lemon Squeezy license activate/validate call.</summary>
public record LicenseResult(bool Ok, string Status, string InstanceId, string ProductId, string CustomerName, string Error)
{
    /// <summary>License expiry (ISO-8601) — the subscription period end; empty/null for lifetime keys.</summary>
    public string ExpiresAt { get; init; } = "";
    /// <summary>The purchased variant id (used with product id to name the plan).</summary>
    public string VariantId { get; init; } = "";

    /// <summary>True if the call succeeded, the license is active, and it's for one of OUR products.</summary>
    public bool IsPro => Ok && Status == "active" && LicenseService.ProductMatches(ProductId);
}

/// <summary>Validates "Pro" license keys against the Lemon Squeezy License API and binds them to this
/// device. The activate/validate endpoints are keyed by the license itself — no API key in the client.</summary>
public class LicenseService
{
    private const string ActivateUrl   = "https://api.lemonsqueezy.com/v1/licenses/activate";
    private const string ValidateUrl   = "https://api.lemonsqueezy.com/v1/licenses/validate";
    private const string DeactivateUrl = "https://api.lemonsqueezy.com/v1/licenses/deactivate";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public LicenseService() => _http.DefaultRequestHeaders.Add("Accept", "application/json");

    /// <summary>A license unlocks Pro if it's for one of our configured products (or any, in dev when none
    /// are configured).</summary>
    public static bool ProductMatches(string productId)
    {
        var ids = BusinessConfig.Current.AcceptedProductIds;
        return ids.Count == 0 || ids.Contains(productId);
    }

    /// <summary>Activates a key on THIS device (creates/uses a Lemon Squeezy instance; the product's
    /// activation limit caps how many devices a key works on).</summary>
    public async Task<LicenseResult> ActivateAsync(string licenseKey)
    {
        var body = new Dictionary<string, string>
        {
            ["license_key"]   = licenseKey.Trim(),
            ["instance_name"] = DeviceId.InstanceName,
        };
        return await PostAsync(ActivateUrl, body, "activated");
    }

    /// <summary>Re-validates a previously activated key for this device's instance.</summary>
    public async Task<LicenseResult> ValidateAsync(string licenseKey, string instanceId)
    {
        var body = new Dictionary<string, string> { ["license_key"] = licenseKey.Trim() };
        if (!string.IsNullOrEmpty(instanceId)) body["instance_id"] = instanceId;
        return await PostAsync(ValidateUrl, body, "valid");
    }

    /// <summary>Frees this device's seat (e.g. when removing the license).</summary>
    public async Task<bool> DeactivateAsync(string licenseKey, string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return true;
        var body = new Dictionary<string, string> { ["license_key"] = licenseKey.Trim(), ["instance_id"] = instanceId };
        var r = await PostAsync(DeactivateUrl, body, "deactivated");
        return r.Ok;
    }

    private async Task<LicenseResult> PostAsync(string url, Dictionary<string, string> body, string okField)
    {
        try
        {
            using var resp = await _http.PostAsync(url, new FormUrlEncodedContent(body));
            return ParseResponse(await resp.Content.ReadAsStringAsync(), okField);
        }
        catch (Exception ex) { return new LicenseResult(false, "", "", "", "", ex.Message); }
    }

    /// <summary>Parses a license API response. (Static = unit-testable.) okField is "activated"/"valid"/"deactivated".</summary>
    public static LicenseResult ParseResponse(string json, string okField)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            bool ok = r.TryGetProperty(okField, out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var err = Str(r, "error");

            string status = "", instanceId = "", productId = "", customer = "", expiresAt = "", variantId = "";
            if (r.TryGetProperty("license_key", out var lk) && lk.ValueKind == JsonValueKind.Object)
            {
                status = Str(lk, "status");
                expiresAt = Str(lk, "expires_at");   // subscription period end; null/empty for lifetime
            }
            if (r.TryGetProperty("instance", out var inst) && inst.ValueKind == JsonValueKind.Object)
                instanceId = Str(inst, "id");
            if (r.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                productId = NumOrStr(meta, "product_id");
                variantId = NumOrStr(meta, "variant_id");
                customer = Str(meta, "customer_name");
            }
            return new LicenseResult(ok, status, instanceId, productId, customer, err) { ExpiresAt = expiresAt, VariantId = variantId };
        }
        catch (Exception ex) { return new LicenseResult(false, "", "", "", "", "Bad response: " + ex.Message); }
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string NumOrStr(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }
}
