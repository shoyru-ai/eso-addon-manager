using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using EsoAddons.Services;

namespace EsoAddons;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless self-update mode (used for testing + a silent-update entry point):
        //   "Shoyru's ESO Addons.exe" --selfupdate
        if (e.Args.Contains("--selfupdate"))
        {
            _ = RunSelfUpdateAsync();
            return;
        }

        // Optional folder override (used to give the sandbox a clean, junction-free AddOns folder):
        //   "Shoyru's ESO Addons.exe" --addons "C:\path\to\AddOns"
        var addonsOverride = CliArgs.GetOption(e.Args, "--addons");
        new MainWindow(addonsOverride).Show();
    }

    private async Task RunSelfUpdateAsync()
    {
        try
        {
            Diag.Log($"--selfupdate: current={UpdateChecker.CurrentVersion}");
            var info = await new UpdateChecker().CheckAsync();
            Diag.Log($"--selfupdate: available={info?.IsNewer} latest={info?.Version} exe={info?.ExeUrl}");
            if (info is { IsNewer: true } && info.ExeUrl.Length > 0)
            {
                var ok = await AppUpdater.DownloadAndApplyAsync(info.ExeUrl);
                Diag.Log($"--selfupdate: apply ok={ok}");
            }
        }
        catch (System.Exception ex) { Diag.Log("--selfupdate threw: " + ex); }
        finally { Shutdown(); }
    }
}
