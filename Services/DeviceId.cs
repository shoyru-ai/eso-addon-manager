using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace EsoAddons.Services;

/// <summary>A stable per-machine id used to bind a license to a device (Lemon Squeezy "instance").</summary>
public static class DeviceId
{
    private static string? _cached;

    /// <summary>Short, stable fingerprint derived from the Windows MachineGuid (hashed; not reversible).</summary>
    public static string Current => _cached ??= Compute();

    /// <summary>Human-readable instance name shown in the Lemon Squeezy dashboard.</summary>
    public static string InstanceName => $"{Environment.MachineName} ({Current})";

    private static string Compute()
    {
        string raw;
        try
        {
            raw = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string
                  ?? Environment.MachineName;
        }
        catch { raw = Environment.MachineName; }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ShoyruESOAddons|" + raw));
        return Convert.ToHexString(hash)[..16];
    }
}
