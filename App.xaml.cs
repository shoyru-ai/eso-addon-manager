using System.Linq;
using System.Windows;
using EsoAddons.Services;
using Velopack;

namespace EsoAddons;

public partial class App : Application
{
    /// <summary>Custom entry point. Velopack must run FIRST — it handles the install / update / uninstall
    /// hooks (and exits early during those) before any WPF UI is created.</summary>
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();   // loads Application.Resources (theme, styles, converters)
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // With a custom Main, StartupEventArgs.Args isn't populated — read the real command line.
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        // PPE/staging channel: receive pre-release builds. Triggered by --ppe OR by being installed under
        // the separate PPE Velopack identity (install path contains "Shoyru.AddonSuite.PPE") — the latter
        // makes it robust even though Velopack-generated shortcuts don't carry the --ppe arg.
        var ppe = args.Any(a => a.Equals("--ppe", StringComparison.OrdinalIgnoreCase))
                  || AppContext.BaseDirectory.Contains("Shoyru.AddonSuite.PPE", StringComparison.OrdinalIgnoreCase);

        // Optional folder override (e.g. a clean sandbox AddOns folder for testing).
        var addonsOverride = CliArgs.GetOption(args, "--addons");

        new MainWindow(addonsOverride, ppe).Show();
    }
}
