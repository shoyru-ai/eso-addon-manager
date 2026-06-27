namespace EsoAddons.Models;

/// <summary>A single dependency of an addon/library and whether it's installed + installable.</summary>
public class DependencyStatus
{
    public string Name { get; init; } = "";
    public bool IsInstalled { get; init; }
    public bool IsOptional { get; init; }
    /// <summary>True if this dependency exists on ESOUI and can be installed via "Get".</summary>
    public bool IsGettable { get; init; }

    // ✓ installed · ✗ actionable-missing (required + on ESOUI) · – everything else
    public string Glyph => IsInstalled ? "✓" : (IsMissingRequired ? "✗" : "–");
    public string Label => IsOptional ? $"{Name}  (optional)" : Name;

    /// <summary>Only offer "Get" when it's missing AND available on ESOUI.</summary>
    public bool ShowGet => !IsInstalled && IsGettable;

    public string StateText =>
        IsInstalled ? "installed"
        : IsMissingRequired ? "missing"
        : IsOptional ? (IsGettable ? "optional" : "optional · not on ESOUI")
        : "not on ESOUI · likely bundled";

    // glyph colour states:
    //  - red (IsMissingRequired): a required dependency that's missing AND obtainable on ESOUI — the only
    //    actionable problem (shows a Get button).
    //  - muted (IsMissingSoft): optional deps, or required deps not on ESOUI (almost always bundled inside
    //    the addon) — informational, not an error.
    public bool IsMissingRequired => !IsInstalled && !IsOptional && IsGettable;
    public bool IsMissingSoft => !IsInstalled && !IsMissingRequired;
}
