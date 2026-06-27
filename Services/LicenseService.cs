using System.Net.Http;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>Result of a Lemon Squeezy license activate/validate call.</summary>
public record LicenseResult(bool Ok, string Status, string InstanceId, string ProductId, string CustomerName, string Error)
{
    /// <summary>True if the call succeeded, the license is active, and it's for OUR product.</summary>
    public bool IsPro => Ok && Status == "active" && LicenseService.ProductMatches(ProductId);
}

/// <summary>Validates "Pro" license keys against the Lemon Squeezy License API and binds them to this
/// device. The activate/validate endpoints are keyed by the license itself — no API key in the client.</summary>
public class LicenseService
{
    // ----- TODO: fill these in after creating the "Pro" product in Lemon Squeezy -----
    /// <summary>Your Pro product id (from the Lemon Squeezy product). Empty = accept any (dev mode).
    /// Set this so keys from other Lemon Squeezy products can't unlock Pro.</summary>
    public const string ExpectedProductId = "1178447";
    /// <summary>The Pro checkout link (Lemon Squeezy "Buy" URL).</summary>
    public const string CheckoutUrl = "https://shoyruai.lemonsqueezy.com/checkout/buy/72acd5b2-5542-48a2-8cef-e94bf3333ada";
    /// <summary>Optional "Support Shoyru" donation link.</summary>
    public const string SupportUrl = "https://ko-fi.com/shoyru";
    // ---------------------------------------------------------------------------------

    private const string ActivateUrl   = "https://api.lemonsqueezy.com/v1/licenses/activate";
    private const string ValidateUrl   = "https://api.lemonsqueezy.com/v1/licenses/validate";
    private const string DeactivateUrl = "https://api.lemonsqueezy.com/v1/licenses/deactivate";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public LicenseService() => _http.DefaultRequestHeaders.Add("Accept", "application/json");

    public static bool ProductMatches(string productId) =>
        ExpectedProductId.Length == 0 || productId == ExpectedProductId;

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

            string status = "", instanceId = "", productId = "", customer = "";
            if (r.TryGetProperty("license_key", out var lk) && lk.ValueKind == JsonValueKind.Object)
                status = Str(lk, "status");
            if (r.TryGetProperty("instance", out var inst) && inst.ValueKind == JsonValueKind.Object)
                instanceId = Str(inst, "id");
            if (r.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                productId = NumOrStr(meta, "product_id");
                customer = Str(meta, "customer_name");
            }
            return new LicenseResult(ok, status, instanceId, productId, customer, err);
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
