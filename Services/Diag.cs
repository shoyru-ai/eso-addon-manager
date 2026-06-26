using System.IO;

namespace EsoAddons.Services;

/// <summary>Lightweight file logger for diagnosing the self-update flow.</summary>
public static class Diag
{
    public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "shoyru-eso-addons.log");

    public static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
        catch { /* never throw from logging */ }
    }
}
