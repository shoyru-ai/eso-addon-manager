using System.IO;
using System.Net;
using System.Net.Http;

namespace EsoAddons.Services;

/// <summary>Turns an install/update exception into an actionable diagnosis. Born from two field
/// reports that both died as one-line status messages: a Controlled-Folder-Access block (every write
/// into Documents\...\AddOns rejected) and a Cloudflare 403 on ESOUI's CDN (geo/VPN filtering).</summary>
public static class InstallFailure
{
    public enum Kind
    {
        /// <summary>ESOUI's server refused us (403) — VPN, region filtering, or a rate-limit ban.</summary>
        CdnForbidden,
        /// <summary>Connection-level problem (offline, timeout, DNS).</summary>
        Network,
        /// <summary>The filesystem rejected our writes — ransomware protection / AV folder guard,
        /// read-only leftovers, or a dead junction in the AddOns path.</summary>
        WriteBlocked,
        Other,
    }

    /// <summary>Classifies an exception thrown during install/update. (Static = unit-testable.)</summary>
    public static Kind Classify(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => Kind.CdnForbidden,
        HttpRequestException or TaskCanceledException => Kind.Network,
        UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or IOException
            => Kind.WriteBlocked,
        _ => Kind.Other,
    };

    /// <summary>Performs a real write+delete in the AddOns folder. Returns null when writable, else the
    /// blocking exception — distinguishes "this one addon collided" from "ALL writes are being rejected"
    /// (Controlled Folder Access / AV), which deserves the guided-fix dialog.</summary>
    public static Exception? ProbeWrite(string addonsPath)
    {
        try
        {
            Directory.CreateDirectory(addonsPath);
            var dir = Path.Combine(addonsPath, ".shoyru-write-probe");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "probe.tmp");
            File.WriteAllText(file, "ok");
            File.Delete(file);
            Directory.Delete(dir);
            return null;
        }
        catch (Exception ex) { return ex; }
    }
}
