using System.Diagnostics;
using System.IO;

namespace EsoAddons.Services;

/// <summary>Applies an app self-update. Writes a tiny helper that, after THIS process exits,
/// downloads the new single-file exe, swaps it in (with retries — the exe can stay locked for a
/// moment after exit), and relaunches. Because the download runs in the helper, the app can close
/// immediately instead of waiting on a ~70 MB download.</summary>
public static class AppUpdater
{
    /// <summary>Returns true if the helper was started (caller should then shut the app down right away).</summary>
    public static Task<bool> DownloadAndApplyAsync(string exeUrl)
    {
        var current = Environment.ProcessPath;
        Diag.Log($"AppUpdater: processPath={current} url={exeUrl}");
        if (string.IsNullOrEmpty(current) || string.IsNullOrWhiteSpace(exeUrl))
            return Task.FromResult(false);

        var pid     = Environment.ProcessId;
        var log     = Diag.LogPath;
        var tempExe = Path.Combine(Path.GetTempPath(), "ESOAddons.update.exe");
        var cmdPath = Path.Combine(Path.GetTempPath(), "ESOAddons.update.cmd");

        // Batch helper. Note: this is a .cmd FILE, so FOR vars use %%; plain vars use single %.
        // goto-style loops re-parse each line, so %tries% updates without delayed expansion.
        var script =
$@"@echo off
echo helper start pid={pid} >> ""{log}""
:waitloop
tasklist /FI ""PID eq {pid}"" 2>nul | find ""{pid}"" >nul
if not errorlevel 1 ( ping -n 2 127.0.0.1 >nul & goto waitloop )
echo helper: process exited, downloading >> ""{log}""
del ""{tempExe}"" >nul 2>&1
curl.exe -L -s -o ""{tempExe}"" ""{exeUrl}""
echo helper: curl errorlevel=%errorlevel% >> ""{log}""
set sz=0
for %%A in (""{tempExe}"") do set sz=%%~zA
echo helper: downloaded size=%sz% >> ""{log}""
if %sz% LSS 1000000 (
  echo helper: download failed/too small - relaunching current build >> ""{log}""
  start """" ""{current}""
  goto cleanup
)
set tries=0
:copyloop
copy /Y ""{tempExe}"" ""{current}"" >nul 2>&1
if not errorlevel 1 goto copied
set /a tries+=1
echo helper: copy locked, retry %tries% >> ""{log}""
if %tries% GEQ 20 (
  echo helper: copy still failing - relaunching current build >> ""{log}""
  start """" ""{current}""
  goto cleanup
)
ping -n 2 127.0.0.1 >nul
goto copyloop
:copied
echo helper: swapped OK - relaunching new build >> ""{log}""
start """" ""{current}""
:cleanup
del ""{tempExe}"" >nul 2>&1
(goto) 2>nul & del ""%~f0""
";
        File.WriteAllText(cmdPath, script);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmdPath}\"")
        { CreateNoWindow = true, UseShellExecute = false });
        Diag.Log("AppUpdater: helper started (downloads + swaps after this process exits)");
        return Task.FromResult(true);
    }
}
