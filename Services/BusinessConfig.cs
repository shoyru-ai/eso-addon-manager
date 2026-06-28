using System.IO;
using System.Reflection;
using System.Text.Json;

namespace EsoAddons.Services;

/// <summary>A single purchasable plan shown on the Get Pro screen.</summary>
public class PlanInfo
{
    public string Name { get; set; } = "";          // "Monthly", "Annual", "Lifetime"
    public string Price { get; set; } = "";         // "$19.99"
    public string Period { get; set; } = "";        // "/mo", "/yr", "one-time"
    public string OriginalPrice { get; set; } = ""; // struck-through anchor, e.g. "$35.88" (optional)
    public string Badge { get; set; } = "";         // "Save 44%" / "Best value" (optional)
    public string Tagline { get; set; } = "";       // short note under the price (optional)
    public string CheckoutUrl { get; set; } = "";
    public bool Recurring { get; set; }             // true for subscriptions
    public int TrialDays { get; set; }              // 0 = no trial; >0 shows "Start N-day free trial"
    /// <summary>ISO date; if set, the plan is only offered until then (e.g. a launch-only Founder's Lifetime),
    /// after which it auto-hides. Empty = always available.</summary>
    public string AvailableUntil { get; set; } = "";

    /// <summary>False once AvailableUntil has passed.</summary>
    public bool IsAvailable =>
        string.IsNullOrWhiteSpace(AvailableUntil) ||
        (DateTimeOffset.TryParse(AvailableUntil, out var d) && d > DateTimeOffset.Now);

    /// <summary>"only until Jan 1, 2027" for limited plans, else empty.</summary>
    public string AvailabilityNote =>
        !string.IsNullOrWhiteSpace(AvailableUntil) && DateTimeOffset.TryParse(AvailableUntil, out var d)
            ? $"only until {d.LocalDateTime:MMM d, yyyy}" : "";
}

/// <summary>Business/monetization config (product IDs, checkout URLs, prices, backend URL). Loaded from an
/// EMBEDDED, git-ignored "appconfig.json" so none of it lives in the public source. Absent (e.g. a public
/// clone) -> harmless empty defaults. The committed code here is generic; the values are not in git.</summary>
public class BusinessConfig
{
    /// <summary>Base URL of the subscription-management backend (…/api). Empty = feature hidden.</summary>
    public string BackendBaseUrl { get; set; } = "";
    /// <summary>Optional donation link.</summary>
    public string SupportUrl { get; set; } = "";
    /// <summary>Product ids whose license keys unlock Pro (subscription + lifetime). Empty = accept any (dev).</summary>
    public List<string> AcceptedProductIds { get; set; } = new();
    /// <summary>Plans shown on the Get Pro screen (monthly/annual/lifetime).</summary>
    public List<PlanInfo> Plans { get; set; } = new();
    /// <summary>product_id -> friendly plan name (each plan is its own LS product), for the Manage Pro
    /// "you're on the X plan" line.</summary>
    public Dictionary<string, string> PlanNamesByProductId { get; set; } = new();

    private static BusinessConfig? _current;
    /// <summary>The loaded config (cached). Never throws — returns empty defaults if the file is absent.</summary>
    public static BusinessConfig Current => _current ??= Load();

    private static BusinessConfig Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("appconfig.json", StringComparison.OrdinalIgnoreCase));
            if (name is null) return new BusinessConfig();
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            return JsonSerializer.Deserialize<BusinessConfig>(r.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new BusinessConfig();
        }
        catch { return new BusinessConfig(); }
    }
}
