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
            await _vm.AutoUpdateOnLaunchAsync();   // Pro: silently update addons if the user opted in
            await _vm.CheckForAppUpdateAsync();
            Diag.Log($"update check done. available={_vm.AppUpdateAvailable} latest={_vm.AppUpdateVersion} exeUrl={_vm.AppUpdateExeUrl}");
            // Test/headless hook: auto-apply an available update when ESOADDONS_AUTOUPDATE=1.
            if (_vm.AppUpdateAvailable && Environment.GetEnvironmentVariable("ESOADDONS_AUTOUPDATE") == "1")
            {
                Diag.Log("auto-update hook firing…");
                await ApplyAppUpdateAsync();
            }
            MaybeShowOnboarding();
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

    private void ProTools_Click(object sender, RoutedEventArgs e)
        => new ProToolsWindow(_vm) { Owner = this }.ShowDialog();

    // Read the combo's live text directly — an editable ComboBox's Text binding doesn't reliably
    // flush to the VM before the button's click fires.
    private void SetCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedInstalled is { } a) _vm.SetCategory(a, CategoryCombo.Text ?? "");
    }

    // ==================== First-run walkthrough (spotlight + card) ====================

    private sealed record WalkStep(int? Tab, Func<FrameworkElement?> Target, string Title, string Body);
    private List<WalkStep>? _walk;
    private int _walkIndex;

    private List<WalkStep> BuildWalk()
    {
        // The Pro step adapts to the user's tier: free users see "Get Pro" highlighted, Pro users see "Pro Tools".
        FrameworkElement proTarget = _vm.IsPro ? ProToolsButton : GetProButton;
        var proTitle = _vm.IsPro ? "Pro Tools" : "Unlock more with Pro";
        var proBody = _vm.IsPro
            ? "Pro Tools holds your profiles / loadouts, backups, and multi-PC sync — plus auto-update-on-launch. Themes and addon categories are unlocked too."
            : "Pro adds Update-All & auto-update-on-launch, dark / light themes, addon categories, and profiles, backups & multi-PC sync. Your addons are always free — Pro is just for the manager. Click “Get Pro” to see the full comparison.";

        return new List<WalkStep>
        {
            new(null, () => null, "Welcome to Shoyru Addon Suite",
                "The complete addon manager for ESO. This quick tour shows what you can do — in both the free and Pro versions. You can skip anytime."),
            new(0, () => MainTabs, "Your installed addons",
                "The Installed tab lists every addon you have. Search it, sort any column (including the “Released” date), see installed vs. latest versions, and remove addons. All free."),
            new(1, () => MainTabs, "Browse all of ESOUI",
                "Switch to Browse to search the entire ESOUI catalog and install any addon in one click — filter by category, sort by downloads or release date. Free."),
            new(0, () => DetailPane, "Details & dependencies",
                "Click any addon to see its description, what’s new, and its dependencies — with one-click “Get” for any missing library. Free."),
            new(null, () => proTarget, proTitle, proBody),
            new(null, () => null, "You’re all set!",
                "That’s the tour. If your AddOns folder wasn’t auto-detected, use “Change folder…” up top — then browse and install. Replay this anytime with the “?” button."),
        };
    }

    private void StartWalkthrough()
    {
        _vm.WalkthroughSeen = true;   // mark immediately so it won't auto-open again
        StartTour(BuildWalk());
    }

    /// <summary>Runs any spotlight tour (the full first-run walkthrough, or a post-update "what's new" set).</summary>
    private void StartTour(List<WalkStep> steps)
    {
        if (steps.Count == 0) return;
        _walk = steps;
        _walkIndex = 0;
        WalkthroughHost.Visibility = Visibility.Visible;
        ShowWalkStep();
    }

    /// <summary>First-run full walkthrough, or after an update a "what's new" tour of just the new features.
    /// Stamps the current version so a given version's tour only shows once.</summary>
    private void MaybeShowOnboarding()
    {
        var cur = UpdateChecker.CurrentVersion;
        if (!_vm.WalkthroughSeen)
        {
            StartWalkthrough();   // brand-new user → full tour
        }
        else if (!string.IsNullOrEmpty(_vm.LastSeenVersion) && VersionCompare.IsNewer(cur, _vm.LastSeenVersion))
        {
            var items = WhatsNewSince(_vm.LastSeenVersion);
            if (items.Count > 0) ShowWhatsNewDialog(cur, items);
        }
        _vm.LastSeenVersion = cur;   // remember this version on both paths
    }

    /// <summary>Curated registry of MEANINGFUL, workflow-affecting changes — one entry per spotlight-worthy
    /// feature, tagged with the version it shipped. RULE: only add an entry for a genuinely new button /
    /// feature / piece of functionality. Text tweaks, restyles, and bug fixes do NOT go here (release notes
    /// cover those) and must never trigger a spotlight. The post-update "What's new" tour shows the entries
    /// newer than the version last launched.</summary>
    private List<(string Version, string Summary, WalkStep Step)> FeatureSpotlights() => new()
    {
        ("0.3.18", "Pro Tools — profiles, backups, multi-PC sync & auto-update",
            new WalkStep(null, () => _vm.IsPro ? ProToolsButton : GetProButton, "Pro Tools",
                "Profiles / loadouts, backups, multi-PC sync, and auto-update-on-launch all live here (Pro).")),
        ("0.3.20", "Addon categories + a sortable “Released” date",
            new WalkStep(0, () => MainTabs, "Categories & “Released” date",
                "Installed addons now show a Category (auto-filled from ESOUI, and overridable) plus a sortable “Released” date. Click any header to sort.")),
        ("0.3.22", "First-run walkthrough + “?” replay button",
            new WalkStep(null, () => HelpButton, "Replay any time",
                "The new “?” button replays this welcome walkthrough whenever you want.")),
    };

    private List<(string Version, string Summary, WalkStep Step)> WhatsNewSince(string lastSeen)
    {
        var cur = UpdateChecker.CurrentVersion;
        return FeatureSpotlights()
            .Where(f => VersionCompare.IsNewer(f.Version, lastSeen)        // newer than the version last launched
                     && !VersionCompare.IsNewer(f.Version, cur))           // and already shipped (≤ current)
            .ToList();
    }

    private void ShowWalkStep()
    {
        if (_walk is null) return;
        var step = _walk[_walkIndex];
        WalkStepCounter.Text = $"Step {_walkIndex + 1} of {_walk.Count}";
        WalkTitle.Text = step.Title;
        WalkBody.Text = step.Body;
        WalkBack.Visibility = _walkIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        WalkSkip.Visibility = _walkIndex == _walk.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
        WalkNext.Content = _walkIndex == _walk.Count - 1 ? "Done" : "Next ▶";

        if (step.Tab is int t && MainTabs.SelectedIndex != t) MainTabs.SelectedIndex = t;

        // Let the tab switch + layout settle, then place the spotlight over the target.
        Dispatcher.BeginInvoke(new Action(() => PositionWalk(step)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void PositionWalk(WalkStep step)
    {
        double w = WalkthroughHost.ActualWidth, h = WalkthroughHost.ActualHeight;
        var outer = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, w, h));

        var target = step.Target();
        if (target is null || !target.IsVisible || target.ActualWidth < 1 || target.ActualHeight < 1)
        {
            // No target → full dim, centered card.
            WalkDim.Data = outer;
            WalkSpot.Visibility = Visibility.Collapsed;
            WalkCard.HorizontalAlignment = HorizontalAlignment.Center;
            WalkCard.VerticalAlignment = VerticalAlignment.Center;
            WalkCard.Margin = new Thickness(0);
            return;
        }

        var tl = target.TransformToVisual(WalkthroughHost).Transform(new Point(0, 0));
        var rect = new Rect(tl.X, tl.Y, target.ActualWidth, target.ActualHeight);
        rect.Inflate(6, 6);
        rect.Intersect(new Rect(0, 0, w, h));

        // Dim everything except a rounded hole over the target (even-odd fills the ring only).
        var grp = new System.Windows.Media.GeometryGroup { FillRule = System.Windows.Media.FillRule.EvenOdd };
        grp.Children.Add(outer);
        grp.Children.Add(new System.Windows.Media.RectangleGeometry(rect, 8, 8));
        WalkDim.Data = grp;

        WalkSpot.Visibility = Visibility.Visible;
        WalkSpot.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        WalkSpot.Width = rect.Width;
        WalkSpot.Height = rect.Height;

        // Card goes opposite the spotlight so it never covers it.
        WalkCard.HorizontalAlignment = HorizontalAlignment.Center;
        if (rect.Bottom < h / 2)
        {
            WalkCard.VerticalAlignment = VerticalAlignment.Top;
            WalkCard.Margin = new Thickness(0, Math.Min(rect.Bottom + 18, Math.Max(18, h - 240)), 0, 0);
        }
        else
        {
            WalkCard.VerticalAlignment = VerticalAlignment.Bottom;
            WalkCard.Margin = new Thickness(0, 0, 0, Math.Min(h - rect.Top + 18, Math.Max(18, h - 240)));
        }
    }

    private void WalkNext_Click(object sender, RoutedEventArgs e)
    {
        if (_walk is null) return;
        if (_walkIndex >= _walk.Count - 1) { EndWalkthrough(); return; }
        _walkIndex++;
        ShowWalkStep();
    }

    private void WalkBack_Click(object sender, RoutedEventArgs e)
    {
        if (_walkIndex > 0) { _walkIndex--; ShowWalkStep(); }
    }

    private void WalkSkip_Click(object sender, RoutedEventArgs e) => EndWalkthrough();

    private void EndWalkthrough()
    {
        WalkthroughHost.Visibility = Visibility.Collapsed;
        _walk = null;
    }

    private void ReplayWalkthrough_Click(object sender, RoutedEventArgs e) => StartWalkthrough();

    /// <summary>Post-update dialog: lists the new meaningful features and offers a "Show me" spotlight tour.</summary>
    private void ShowWhatsNewDialog(string version, List<(string Version, string Summary, WalkStep Step)> items)
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = $"What's New - v{version}",
            Width = 480, MinWidth = 420,
            SizeToContent = SizeToContent.Height, MaxHeight = 560,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"What's new in v{version}", Foreground = B("Text", "#E6E6E6"),
            FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(header, 0); grid.Children.Add(header);

        var notes = "Here's what changed since you last used the app:\n\n"
                  + string.Join("\n", items.Select(i => "- " + i.Summary));
        var body = new Border
        {
            Background = B("Panel", "#252526"), BorderBrush = B("Border", "#3A3A3A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0),
            Child = BuildNotesView(notes, B),
        };
        Grid.SetRow(body, 1); grid.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var showMe = new Button { Content = "Show me what's new ▶", Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Primary") is Style ps) showMe.Style = ps;
        showMe.Click += (_, _) => { win.Close(); StartTour(items.Select(i => i.Step).ToList()); };
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        if (TryFindResource("Ghost") is Style gs) close.Style = gs;
        close.Click += (_, _) => win.Close();
        buttons.Children.Add(showMe); buttons.Children.Add(close);
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons);

        win.Content = grid;
        win.ShowDialog();
    }

    private void ShowLicenseDialog()
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = "Shoyru Addon Suite - Pro",
            Width = 540, MinWidth = 500,
            SizeToContent = SizeToContent.Height,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(18) };

        if (_vm.IsPro)
        {
            // Blue verified-style badge (matches the header), not a generic green check.
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var badge = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
            badge.Children.Add(new System.Windows.Shapes.Ellipse { Fill = B("Accent", "#5B8DEF") });
            badge.Children.Add(new TextBlock
            {
                Text = "✓", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(badge);
            header.Children.Add(new TextBlock
            {
                Text = "Pro is active on this device", Foreground = B("Accent", "#5B8DEF"),
                FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(header);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Unlock Pro", Foreground = B("Text", "#E6E6E6"),
                FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6),
            });
        }
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
            panel.Children.Add(new TextBlock { Text = "License key", Foreground = B("Muted", "#9A9A9A"), FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            var keyBox = new TextBox { FontSize = 15, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 10) };
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
            var removeRow = new StackPanel { Orientation = Orientation.Horizontal };
            var remove = new Button { Content = "Remove from this device", Margin = new Thickness(0, 0, 8, 0) };
            if (TryFindResource("Ghost") is Style gs) remove.Style = gs;
            remove.Click += async (_, _) => { await _vm.RemoveLicenseAsync(); win.Close(); };
            var info = new Button { Content = "ⓘ  What's this?", VerticalAlignment = VerticalAlignment.Center };
            if (TryFindResource("Ghost") is Style gi) info.Style = gi;
            info.Click += (_, _) => MessageBox.Show(win,
                "“Remove from this device” deactivates Pro on THIS computer and frees its activation seat.\n\n" +
                "Use it when you want to move your license to another PC — each key works on a limited number of devices. " +
                "Your purchase isn't lost: just re-enter your license key here anytime to reactivate Pro on this or another machine.",
                "Remove from this device", MessageBoxButton.OK, MessageBoxImage.Information);
            removeRow.Children.Add(remove);
            removeRow.Children.Add(info);
            panel.Children.Add(removeRow);
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
            ("See & install dependencies",      true,  true),
            ("Update addons (incl. Update All)",false, true),
            ("Auto-install dependencies",       false, true),
            ("Auto-update on launch",           false, true),
            ("Dark / light theme",              false, true),
            ("Organize with categories",        false, true),
            ("Profiles / loadouts",             false, true),
            ("Backups & restore",               false, true),
            ("Multi-PC sync",                   false, true),
        };

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
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
                Text = you ? t + " (you)" : t,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Bold, FontSize = 14,
                Foreground = you ? B("Accent", "#5B8DEF") : B("Text", "#E6E6E6"),
                Margin = new Thickness(0, 0, 0, 7),
            };
            return tb;
        }
        var featHdr = new TextBlock
        {
            Text = "Feature", FontWeight = FontWeights.Bold, FontSize = 14, Foreground = B("Muted", "#9A9A9A"),
            Margin = new Thickness(0, 0, 0, 7), VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(featHdr, 0); Grid.SetRow(featHdr, 0); grid.Children.Add(featHdr);
        var fh = Hdr("Free", yourCol == 1); Grid.SetColumn(fh, 1); Grid.SetRow(fh, 0); grid.Children.Add(fh);
        var ph = Hdr("Pro", yourCol == 2); Grid.SetColumn(ph, 2); Grid.SetRow(ph, 0); grid.Children.Add(ph);

        for (int i = 0; i < rows.Length; i++)
        {
            var (feat, free, pro) = rows[i];
            var ft = new TextBlock
            {
                Text = feat, Foreground = B("Text", "#E6E6E6"), FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(ft, 0); Grid.SetRow(ft, i + 1); grid.Children.Add(ft);

            TextBlock Mark(bool has) => new()
            {
                Text = has ? "✓" : "—", TextAlignment = TextAlignment.Center, FontSize = 15,
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
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
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

        var pw = new PasswordBox { FontSize = 16, Padding = new Thickness(6, 5, 6, 5) };
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
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
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
            FontSize = 20, FontWeight = FontWeights.Bold,
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
                Child = BuildNotesView(notes, B),
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

    /// <summary>Renders aggregated release notes line-by-line, bolding version headers (vX.Y.Z) and section
    /// headers. A line is treated as a section header if it's "## Foo", "**Foo**", or a short line ending in
    /// ":" — so notes read as a structured changelog instead of a flat blob.</summary>
    private UIElement BuildNotesView(string notes, Func<string, string, Brush> B)
    {
        var panel = new StackPanel();
        foreach (var raw in notes.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { panel.Children.Add(new Border { Height = 5 }); continue; }

            // divider rows from the per-version aggregation
            if (line.Length >= 4 && line.All(c => c is '─' or '-' or '—'))
            {
                panel.Children.Add(new Border
                {
                    Height = 1, Background = B("Border", "#3A3A3A"),
                    Margin = new Thickness(0, 8, 0, 8),
                });
                continue;
            }

            bool isVersion = line.Length > 1 && (line[0] is 'v' or 'V') && char.IsDigit(line[1]);
            string? headerText =
                line.StartsWith("## ") ? line[3..].Trim()
                : line.StartsWith("**") && line.EndsWith("**") && line.Length > 4 ? line[2..^2].Trim()
                : !line.StartsWith("-") && line.EndsWith(":") && line.Length <= 42 ? line
                : null;

            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20, Foreground = B("Text", "#E6E6E6") };
            if (isVersion)
            {
                tb.Text = line; tb.FontWeight = FontWeights.Bold; tb.FontSize = 16;
                tb.Foreground = B("Accent", "#5B8DEF"); tb.Margin = new Thickness(0, 6, 0, 4);
            }
            else if (headerText is not null)
            {
                tb.Text = headerText; tb.FontWeight = FontWeights.Bold; tb.FontSize = 14;
                tb.Margin = new Thickness(0, 7, 0, 2);
            }
            else
            {
                tb.Text = line; tb.FontSize = 14; tb.Margin = new Thickness(0, 1, 0, 1);
            }
            panel.Children.Add(tb);
        }
        return panel;
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
