namespace EsoAddons.Models;

/// <summary>A single dependency of an addon and whether it's currently installed.</summary>
public class DependencyStatus
{
    public string Name { get; init; } = "";
    public bool IsInstalled { get; init; }
    public bool IsOptional { get; init; }

    public string Glyph => IsInstalled ? "✓" : "✗";   // ✓ / ✗
    public string Label => IsOptional ? $"{Name}  (optional)" : Name;
    public string StateText => IsInstalled ? "installed" : (IsOptional ? "not installed" : "MISSING");
}
