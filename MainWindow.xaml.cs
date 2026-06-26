using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using EsoAddons.Services;
using EsoAddons.ViewModels;

namespace EsoAddons;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            Diag.Log($"Loaded fired. version={UpdateChecker.CurrentVersion} autoupdate_env={Environment.GetEnvironmentVariable("ESOADDONS_AUTOUPDATE")}");
            await _vm.LoadAsync();
            Diag.Log("LoadAsync done. checking for app update…");
            await _vm.CheckForAppUpdateAsync();
            Diag.Log($"update check done. available={_vm.AppUpdateAvailable} latest={_vm.AppUpdateVersion} exeUrl={_vm.AppUpdateExeUrl}");
            // Test/headless hook: auto-apply an available update when ESOADDONS_AUTOUPDATE=1.
            if (_vm.AppUpdateAvailable && Environment.GetEnvironmentVariable("ESOADDONS_AUTOUPDATE") == "1")
            {
                Diag.Log("auto-update hook firing…");
                await ApplyAppUpdateAsync();
            }
        };
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select your ESO AddOns folder (…\\Elder Scrolls Online\\live\\AddOns)",
        };
        if (Directory.Exists(_vm.AddonsPath)) dialog.InitialDirectory = _vm.AddonsPath;
        if (dialog.ShowDialog(this) == true)
            _vm.SetAddonsPath(dialog.FolderName);
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e) => await ApplyAppUpdateAsync();

    private void WhatsNew_Click(object sender, RoutedEventArgs e) => OpenUrl(_vm.AppUpdateReleaseUrl);

    private async System.Threading.Tasks.Task ApplyAppUpdateAsync()
    {
        var url = _vm.AppUpdateExeUrl;
        Diag.Log($"ApplyAppUpdateAsync url={url}");
        if (string.IsNullOrEmpty(url)) { OpenUrl(_vm.AppUpdateReleaseUrl); return; }

        _vm.Status = $"Downloading update v{_vm.AppUpdateVersion}…";
        bool ok;
        try { ok = await AppUpdater.DownloadAndApplyAsync(url); }
        catch (Exception ex) { Diag.Log("AppUpdater threw: " + ex); ok = false; }
        Diag.Log($"DownloadAndApplyAsync returned {ok}");
        if (ok) Application.Current.Shutdown();
        else { _vm.Status = "Auto-update failed — opening the download page."; OpenUrl(_vm.AppUpdateReleaseUrl); }
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
