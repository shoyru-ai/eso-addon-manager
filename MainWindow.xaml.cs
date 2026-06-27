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

    public MainWindow(string? addonsOverride = null, bool ppe = false)
    {
        InitializeComponent();
        _vm = new MainViewModel(addonsOverride, ppe);
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            Diag.Log($"Loaded fired. version={UpdateChecker.CurrentVersion} autoupdate_env={Environment.GetEnvironmentVariable("ESOADDONS_AUTOUPDATE")}");
            await _vm.LoadAsync();
            Diag.Log("LoadAsync done. checking license + app update…");
            await _vm.CheckLicenseAsync();
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

    // ---- Custom Addons password gate ----
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only react to the TabControl's own selection (this event also bubbles up from inner lists/grids).
        if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
        if (ReferenceEquals(MainTabs.SelectedItem, CustomAddonsTab) && !_vm.CustomAddonsUnlocked)
            PromptCustomAddonsPassword();
    }

    private void UnlockCustomAddons_Click(object sender, RoutedEventArgs e) => PromptCustomAddonsPassword();

    // ---- Pro license dialog ----
    private void ManageLicense_Click(object sender, RoutedEventArgs e) => ShowLicenseDialog();

    private void ToggleTheme_Click(object sender, RoutedEventArgs e) => _vm.ToggleTheme();

    private void ShowLicenseDialog()
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = "Shoyru Addon Suite - Pro",
            Width = 490, MinWidth = 460,
            SizeToContent = SizeToContent.Height,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"),
            FontFamily = new FontFamily("Segoe UI"),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(18) };

        panel.Children.Add(new TextBlock
        {
            Text = _vm.IsPro ? "✓ Pro is active on this device" : "Unlock Pro",
            Foreground = _vm.IsPro ? B("Good", "#5BBF73") : B("Text", "#E6E6E6"),
            FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(new TextBlock
        {
            Text = _vm.IsPro
                ? "Thanks for supporting development! Premium features are unlocked."
                : "Pro unlocks premium tool features (addon profiles, backups, multi-PC sync, and more). Your addons are always free — Pro is for the manager.",
            Foreground = B("Muted", "#9A9A9A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
        });

        // Free vs Pro comparison — the user's current tier is outlined/highlighted
        panel.Children.Add(BuildComparison(B));

        var status = new TextBlock { Foreground = B("Muted", "#9A9A9A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };

        if (!_vm.IsPro)
        {
            panel.Children.Add(new TextBlock { Text = "License key", Foreground = B("Muted", "#9A9A9A"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            var keyBox = new TextBox { FontSize = 13, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(keyBox);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var activate = new Button { Content = "Activate", Margin = new Thickness(0, 0, 8, 0) };
            if (TryFindResource("Primary") is Style ps) activate.Style = ps;
            var buy = new Button { Content = "Buy Pro" };
            if (TryFindResource("Update") is Style us) buy.Style = us;
            buy.Click += (_, _) => OpenUrl(_vm.ProBuyUrl);
            activate.Click += async (_, _) =>
            {
                activate.IsEnabled = false;
                var msg = await _vm.ActivateLicenseAsync(keyBox.Text);
                status.Text = msg; status.Visibility = Visibility.Visible;
                status.Foreground = _vm.IsPro ? B("Good", "#5BBF73") : B("Danger", "#E06C6C");
                activate.IsEnabled = true;
                if (_vm.IsPro) win.Close();   // unlocked — close; header shows the PRO badge
            };
            row.Children.Add(activate);
            row.Children.Add(buy);
            panel.Children.Add(row);
        }
        else
        {
            var remove = new Button { Content = "Remove from this device", HorizontalAlignment = HorizontalAlignment.Left };
            if (TryFindResource("Ghost") is Style gs) remove.Style = gs;
            remove.Click += async (_, _) => { await _vm.RemoveLicenseAsync(); win.Close(); };
            panel.Children.Add(remove);
        }

        panel.Children.Add(status);

        // footer: support link
        var support = new TextBlock { Margin = new Thickness(0, 16, 0, 0) };
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("♥ Support Shoyru (donate)")) { Foreground = B("Accent", "#3B82F6") };
        link.Click += (_, _) => OpenUrl(_vm.SupportUrl);
        support.Inlines.Add(link);
        panel.Children.Add(support);

        win.Content = panel;
        win.ShowDialog();
    }

    /// <summary>Free vs Pro feature comparison; the user's current tier column is outlined + highlighted.</summary>
    private UIElement BuildComparison(Func<string, string, Brush> B)
    {
        var rows = new (string feat, bool free, bool pro)[]
        {
            ("Browse, search & install addons", true,  true),
            ("Remove addons",                   true,  true),
            ("See required dependencies",       true,  true),
            ("Update addons (incl. Update All)",false, true),
            ("Auto-install dependencies",       false, true),
            ("Dark / light theme",              false, true),
            ("Auto-update on launch",           false, true),
            ("Profiles, backups & PC sync",     false, true),
        };

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in rows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        int yourCol = _vm.IsPro ? 2 : 1;

        // highlight the current tier's whole column
        var hl = new Border
        {
            Background = B("UpdateRow", "#1F5B8DEF"),
            BorderBrush = B("Accent", "#5B8DEF"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(-3, -2, -3, -2),
        };
        Grid.SetColumn(hl, yourCol); Grid.SetRow(hl, 0); Grid.SetRowSpan(hl, rows.Length + 1);
        grid.Children.Add(hl);

        TextBlock Hdr(string t, bool you)
        {
            var tb = new TextBlock
            {
                Text = you ? t + "  (you)" : t,
                TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold, FontSize = 12,
                Foreground = you ? B("Accent", "#5B8DEF") : B("Text", "#E6E6E6"),
                Margin = new Thickness(0, 0, 0, 7),
            };
            return tb;
        }
        var fh = Hdr("Free", yourCol == 1); Grid.SetColumn(fh, 1); Grid.SetRow(fh, 0); grid.Children.Add(fh);
        var ph = Hdr("Pro", yourCol == 2); Grid.SetColumn(ph, 2); Grid.SetRow(ph, 0); grid.Children.Add(ph);

        for (int i = 0; i < rows.Length; i++)
        {
            var (feat, free, pro) = rows[i];
            var ft = new TextBlock
            {
                Text = feat, Foreground = B("Text", "#E6E6E6"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(ft, 0); Grid.SetRow(ft, i + 1); grid.Children.Add(ft);

            TextBlock Mark(bool has) => new()
            {
                Text = has ? "✓" : "—", TextAlignment = TextAlignment.Center, FontSize = 13,
                FontWeight = has ? FontWeights.Bold : FontWeights.Normal,
                Foreground = has ? B("Good", "#5BBF73") : B("Muted", "#9A9A9A"),
                Margin = new Thickness(0, 3, 0, 3),
            };
            var fm = Mark(free); Grid.SetColumn(fm, 1); Grid.SetRow(fm, i + 1); grid.Children.Add(fm);
            var pm = Mark(pro); Grid.SetColumn(pm, 2); Grid.SetRow(pm, i + 1); grid.Children.Add(pm);
        }
        return grid;
    }

    /// <summary>Modal password prompt for the Custom Addons tab. Unlocks for the rest of the session.</summary>
    private void PromptCustomAddonsPassword()
    {
        if (_vm.CustomAddonsUnlocked) return;

        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = "Password Required",
            Width = 400, Height = 220,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"),
            FontFamily = new FontFamily("Segoe UI"),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var grid = new Grid { Margin = new Thickness(18) };
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Enter the password to unlock Shoyru's Custom Addons:",
            Foreground = B("Text", "#E6E6E6"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(label, 0); grid.Children.Add(label);

        var pw = new PasswordBox { FontSize = 14, Padding = new Thickness(6, 5, 6, 5) };
        Grid.SetRow(pw, 1); grid.Children.Add(pw);

        var error = new TextBlock
        {
            Foreground = B("Danger", "#E06C6C"), Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(error, 2); grid.Children.Add(error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var ok = new Button { Content = "Unlock", Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        if (TryFindResource("Primary") is Style ps) ok.Style = ps;
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        if (TryFindResource("Ghost") is Style gs) cancel.Style = gs;
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3); grid.Children.Add(buttons);

        void Attempt()
        {
            if (_vm.CustomAddonsUnlocked) return;
            if (AccessGate.IsCorrect(pw.Password)) { _vm.CustomAddonsUnlocked = true; win.DialogResult = true; }
            else { error.Text = "Incorrect password. Try again."; error.Visibility = Visibility.Visible; pw.Clear(); pw.Focus(); }
        }
        ok.Click += (_, _) => Attempt();

        win.Content = grid;
        win.Loaded += (_, _) => pw.Focus();
        win.ShowDialog();
    }

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
            Title = $"Release Notes - v{_vm.AppUpdateVersion}",
            Width = 520, MinWidth = 360,
            SizeToContent = SizeToContent.Height,   // grow/shrink to fit the notes; long notes cap via the scroller
            MaxHeight = 640,
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
            Text = $"Release Notes — v{_vm.AppUpdateVersion}",
            Foreground = B("Text", "#E6E6E6"),
            FontSize = 18, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 440,   // beyond this, long notes scroll instead of growing the window
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
