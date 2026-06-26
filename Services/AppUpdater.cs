using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace EsoAddons.Services;

/// <summary>Applies an app self-update: downloads the new single-file exe, then launches a tiny helper
/// that waits for this process to exit, swaps the exe, and relaunches it.</summary>
public static class AppUpdater
{
    /// <summary>Returns true if the swap helper was started (caller should then shut the app down).</summary>
    public static async Task<bool> DownloadAndApplyAsync(string exeUrl)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current) || string.IsNullOrWhiteSpace(exeUrl)) return false;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.Add("User-Agent", "ESO-Addons-Updater");
        var bytes = await http.GetByteArrayAsync(exeUrl);
        if (bytes.Length < 1_000_000) return false; // sanity: a real self-contained exe is tens of MB

        var tempExe = Path.Combine(Path.GetTempPath(), "ESOAddons.update.exe");
        await File.WriteAllBytesAsync(tempExe, bytes);

        var pid = Environment.ProcessId;
        var cmdPath = Path.Combine(Path.GetTempPath(), "ESOAddons.update.cmd");
        var script =
$@"@echo off
:loop
tasklist /FI ""PID eq {pid}"" 2>nul | find ""{pid}"" >nul
if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto loop )
copy /Y ""{tempExe}"" ""{current}"" >nul
start """" ""{current}""
del ""{tempExe}"" >nul 2>&1
(goto) 2>nul & del ""%~f0""
";
        await File.WriteAllTextAsync(cmdPath, script);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmdPath}\"")
        { CreateNoWindow = true, UseShellExecute = false });
        return true;
    }
}
