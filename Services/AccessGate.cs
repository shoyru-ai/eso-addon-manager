namespace EsoAddons.Services;

/// <summary>Interim client-side password gate for the Custom Addons tab.
/// NOTE: this is a soft gate only (the password lives in the client and the addon content is
/// public) — it deters casual access until the real device-bound paywall is built. See the
/// monetization plan. Accepts the password case-insensitively, trimmed, for friend-friendliness.</summary>
public static class AccessGate
{
    public const string CustomAddonsPassword = "Neopia";

    public static bool IsCorrect(string? input) =>
        !string.IsNullOrWhiteSpace(input) &&
        string.Equals(input.Trim(), CustomAddonsPassword, StringComparison.OrdinalIgnoreCase);
}
