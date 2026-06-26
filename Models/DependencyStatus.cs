namespace EsoAddons.Models;

/// <summary>A single dependency of an addon/library and whether it's installed + installable.</summary>
public class DependencyStatus
{
    public string Name { get; init; } = "";
    public bool IsInstalled { get; init; }
    public bool IsOptional { get; init; }
    /// <summary>True if this dependency exists on ESOUI and can be installed via "Get".</summary>
    public bool IsGettable { get; init; }

    // ✓ installed · ✗ required-missing · – optional-missing
    public string Glyph => IsInstalled ? "✓" : (IsOptional ? "–" : "✗");
    public string Label => IsOptional ? $"{Name}  (optional)" : Name;

    /// <summary>Only offer "Get" when it's missing AND available on ESOUI.</summary>
    public bool ShowGet => !IsInstalled && IsGettable;

    public string StateText =>
        IsInstalled ? "installed"
        : !IsGettable ? (IsOptional ? "optional · not on ESOUI" : "missing · not on ESOUI")
        : (IsOptional ? "optional" : "missing");

    // glyph colour states
    public bool IsMissingRequired => !IsInstalled && !IsOptional;
    public bool IsMissingOptional => !IsInstalled && IsOptional;
}
