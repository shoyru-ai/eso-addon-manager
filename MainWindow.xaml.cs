using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using EsoAddons.Services;
using EsoAddons.ViewModels;

namespace EsoAddons;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(string? addonsOverride = null)
    {
        InitializeComponent();
        _vm = new MainViewModel(addonsOverride);
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

    private void WhatsNew_Click(object sender, RoutedEventArgs e) => ShowWhatsNew();

    /// <summary>Closeable in-app dialog showing the new version's release notes (instead of opening the repo).</summary>
    private void ShowWhatsNew()
    {
        var notes = string.IsNullOrWhiteSpace(_vm.AppUpdateNotes)
            ? "(No release notes were provided for this version.)"
            : _vm.AppUpdateNotes.Trim();

        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = $"What's New — v{_vm.AppUpdateVersion}",
            Width = 540, Height = 480, MinWidth = 380, MinHeight = 260,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"),
            FontFamily = new FontFamily("Segoe UI"),
            ShowInTaskbar = false,
        };

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"What's New in v{_vm.AppUpdateVersion}",
            Foreground = B("Text", "#E6E6E6"),
            FontSize = 18, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Background = B("Panel", "#252526"),
                BorderBrush = B("Border", "#3A3A3A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Child = new TextBlock
                {
                    Text = notes,
                    Foreground = B("Text", "#E6E6E6"),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13, LineHeight = 19,
                },
            },
        };
        Grid.SetRow(scroller, 1);
        grid.Children.Add(scroller);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var updateBtn = new Button { Content = "Update now", Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Update") is Style us) updateBtn.Style = us;
        updateBtn.Click += async (_, _) => { win.Close(); await ApplyAppUpdateAsync(); };
        var closeBtn = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        if (TryFindResource("Ghost") is Style gs) closeBtn.Style = gs;
        closeBtn.Click += (_, _) => win.Close();
        buttons.Children.Add(updateBtn);
        buttons.Children.Add(closeBtn);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        win.Content = grid;
        win.ShowDialog();
    }

    private async System.Threading.Tasks.Task ApplyAppUpdateAsync()
    {
        var url = _vm.AppUpdateExeUrl;
        Diag.Log($"ApplyAppUpdateAsync url={url}");
        if (string.IsNullOrEmpty(url)) { OpenUrl(_vm.AppUpdateReleaseUrl); return; }

        _vm.Status = $"Updating to v{_vm.AppUpdateVersion}… the app will restart automatically.";
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
