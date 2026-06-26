using System.Diagnostics;
using System.IO;
using System.Linq;

namespace EsoAddons.Services;

/// <summary>Applies an app self-update. Writes a tiny helper that, after THIS process exits,
/// downloads the new single-file exe and swaps it in by RENAME (the new exe is downloaded into the
/// same folder as the running exe, then atomically renamed into place — never byte-copied over the
/// live exe, which previously corrupted the file while Windows still had it memory-mapped). Then it
/// relaunches. The download runs in the helper so the app can close immediately.</summary>
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
        // Stage the download + backup IN THE SAME FOLDER as the running exe so the swap is a
        // same-volume rename (atomic, no byte copy → no corruption).
        var exeDir  = Path.GetDirectoryName(current)!;
        var tempExe = Path.Combine(exeDir, "ESOAddons.update.exe");
        var backup  = Path.Combine(exeDir, "ESOAddons.previous.exe");
        var cmdPath = Path.Combine(Path.GetTempPath(), "ESOAddons.update.cmd");

        // Preserve the original launch args (e.g. --addons "<clean folder>") so the relaunched
        // build behaves identically. Drop --selfupdate so we never relaunch into headless mode.
        static string Quote(string a) => a.Length == 0 || a.Contains(' ') ? $"\"{a}\"" : a;
        var passthru = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)
            .Where(a => !a.Equals("--selfupdate", StringComparison.OrdinalIgnoreCase))
            .Select(Quote));
        var relaunch = passthru.Length > 0
            ? $@"start """" ""{current}"" {passthru}"
            : $@"start """" ""{current}""";

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
  del ""{tempExe}"" >nul 2>&1
  {relaunch}
  goto cleanup
)
REM Swap by RENAME within the same folder (atomic, no byte copy = no corruption).
set tries=0
:swaploop
del ""{backup}"" >nul 2>&1
move /Y ""{current}"" ""{backup}"" >nul 2>&1
if exist ""{current}"" goto swapretry
move /Y ""{tempExe}"" ""{current}"" >nul 2>&1
if exist ""{current}"" goto swapped
move /Y ""{backup}"" ""{current}"" >nul 2>&1
:swapretry
set /a tries+=1
echo helper: swap not ready, retry %tries% >> ""{log}""
if %tries% GEQ 25 (
  echo helper: swap failed - relaunching available build >> ""{log}""
  if not exist ""{current}"" move /Y ""{backup}"" ""{current}"" >nul 2>&1
  {relaunch}
  goto cleanup
)
ping -n 2 127.0.0.1 >nul
goto swaploop
:swapped
echo helper: swapped by rename - relaunching new build >> ""{log}""
{relaunch}
del ""{backup}"" >nul 2>&1
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
