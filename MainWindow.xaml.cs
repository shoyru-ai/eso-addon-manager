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
        // Keep the walkthrough overlay (dim + spotlight) correct when the window resizes/maximizes.
        WalkthroughHost.SizeChanged += (_, _) =>
        {
            if (_walk is not null && WalkthroughHost.Visibility == Visibility.Visible)
                Dispatcher.BeginInvoke(new Action(() => PositionWalk(_walk[_walkIndex])),
                    System.Windows.Threading.DispatcherPriority.Loaded);
        };
        Loaded += async (_, _) =>
        {
            Diag.Log($"Loaded fired. version={UpdateChecker.CurrentVersion}");
            if (!_vm.LanguageChosen) ShowLanguageDialog();   // first run: pick the UI language
            if (!EnsureTermsAccepted()) { Application.Current.Shutdown(); return; }   // must accept to use
            await _vm.LoadAsync();
            Diag.Log("LoadAsync done. checking license…");
            await _vm.CheckLicenseAsync();
            // Greet promptly — right after the catalog + license load, BEFORE the update check/banner,
            // so the walkthrough is the first thing a new user sees (not buried behind network calls).
            MaybeShowOnboarding();
            await _vm.AutoUpdateOnLaunchAsync();   // Pro: silently update addons if the user opted in
            await _vm.CheckForAppUpdateAsync();
            Diag.Log($"update check done. available={_vm.AppUpdateAvailable} latest={_vm.AppUpdateVersion}");
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

    private async void UpdateNow_Click(object sender, RoutedEventArgs e) => await _vm.DownloadAndApplyUpdateAsync();

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

    // ---- Header ⚙ settings menu ----
    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e) => ShowAboutDialog();

    // ---- Language picker (first-run + ⚙ menu) ----
    private void Language_Click(object sender, RoutedEventArgs e) => ShowLanguageDialog();

    private void ShowLanguageDialog()
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = Loc.Instance["Lang_Title"], Width = 360, SizeToContent = SizeToContent.Height,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.Instance["Lang_Title"], FontSize = 18, FontWeight = FontWeights.Bold,
            Foreground = B("Text", "#E6E6E6"), Margin = new Thickness(0, 0, 0, 14),
        });
        var combo = new ComboBox { FontSize = 15 };
        foreach (var (code, name) in Loc.Languages) combo.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(Loc.Languages, l => l.Code == _vm.Language));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is string c) _vm.Language = c; };
        panel.Children.Add(combo);
        var ok = new Button { Content = Loc.Instance["Lang_OK"], HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), IsDefault = true };
        if (TryFindResource("Primary") is Style ps) ok.Style = ps;
        ok.Click += (_, _) => { if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is string c) _vm.Language = c; win.Close(); };
        panel.Children.Add(ok);
        win.Content = panel;
        win.ShowDialog();
    }

    // Read the combo's live text directly — an editable ComboBox's Text binding doesn't reliably
    // flush to the VM before the button's click fires.
    private void SetCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedInstalled is { } a) _vm.SetCategory(a, CategoryCombo.Text ?? "");
    }

    // ==================== First-run walkthrough (spotlight + card) ====================

    /// <summary>Bump this when the first-run walkthrough changes enough to re-greet existing users once.</summary>
    private const int CurrentWalkthroughVersion = 2;

    private sealed record WalkStep(int? Tab, Func<FrameworkElement?> Target, string Title, string Body, Action? OnShow = null);
    private List<WalkStep>? _walk;
    private int _walkIndex;
    private bool _walkIsFirstRun;

    private List<WalkStep> BuildWalk()
    {
        // The Pro step adapts to the user's tier: free users see "Get Pro" highlighted, Pro users see "Pro Tools".
        FrameworkElement proTarget = _vm.IsPro ? ProToolsButton : GetProButton;
        var L = Loc.Instance;
        var proTitle = _vm.IsPro ? L["Walk_ProTitle_Pro"] : L["Walk_ProTitle_Free"];
        var proBody = _vm.IsPro ? L["Walk_ProBody_Pro"] : L["Walk_ProBody_Free"];

        return new List<WalkStep>
        {
            new(null, () => null, L["Walk_WelcomeTitle"], L["Walk_WelcomeBody"]),
            new(0, () => MainTabs, L["Walk_InstalledTitle"], L["Walk_InstalledBody"]),
            new(1, () => MainTabs, L["Walk_BrowseTitle"], L["Walk_BrowseBody"]),
            new(1, () => BrowsePane, L["Walk_DescribeTitle"],
                _vm.IsPro ? L["Walk_DescribeBody_Pro"] : L["Walk_DescribeBody_Free"],
                () => _vm.ShowDescribeDemo()),
            new(0, () => DetailPane, L["Walk_DetailsTitle"], L["Walk_DetailsBody"]),
            new(0, () => DetailPane, L["Walk_TranslateTitle"],
                _vm.IsPro ? L["Walk_TranslateBody_Pro"] : L["Walk_TranslateBody_Free"]),
            new(null, () => proTarget, proTitle, proBody),
            new(null, () => SettingsButton, L["Walk_SetTitle"], L["Walk_SetBody"]),
        };
    }

    private void StartWalkthrough() => StartTour(BuildWalk(), firstRun: true);

    /// <summary>Celebratory tour shown right after a user activates Pro — highlights everything they unlocked.</summary>
    private void StartProTour() => StartTour(BuildProTour(), firstRun: false);

    private List<WalkStep> BuildProTour()
    {
        var L = Loc.Instance;
        return new()
        {
            new(null, () => null, L["Tour_WelcomeTitle"], L["Tour_WelcomeBody"]),
            new(1, () => BrowsePane, L["Tour_DescribeTitle"], L["Tour_DescribeBody"],
                () => _vm.ShowDescribeDemo()),
            new(0, () => DetailPane, L["Tour_TranslateTitle"], L["Tour_TranslateBody"]),
            new(null, () => ProToolsButton, L["Tour_ToolsTitle"], L["Tour_ToolsBody"]),
            new(0, () => MainTabs, L["Tour_CatTitle"], L["Tour_CatBody"]),
            new(1, () => MainTabs, L["Tour_DepsTitle"], L["Tour_DepsBody"]),
            new(null, () => SettingsButton, L["Tour_ThemeTitle"], L["Tour_ThemeBody"]),
            new(null, () => null, L["Tour_EnjoyTitle"], L["Tour_EnjoyBody"]),
        };
    }

    /// <summary>Runs any spotlight tour: the full first-run walkthrough (firstRun=true, marks the walkthrough
    /// version complete when it ends) or a post-update "what's new" set (firstRun=false).</summary>
    private void StartTour(List<WalkStep> steps, bool firstRun)
    {
        if (steps.Count == 0) return;
        _walk = steps;
        _walkIndex = 0;
        _walkIsFirstRun = firstRun;
        WalkthroughHost.Visibility = Visibility.Visible;
        ShowWalkStep();
    }

    /// <summary>First-run full walkthrough (also re-greets once when the walkthrough version is bumped), or
    /// after an update a "what's new" tour of just the new features.</summary>
    private void MaybeShowOnboarding()
    {
        var cur = UpdateChecker.CurrentVersion;
        if (_vm.WalkthroughVersion < CurrentWalkthroughVersion)
        {
            StartWalkthrough();   // never completed the current walkthrough → greet
        }
        else if (!string.IsNullOrEmpty(_vm.LastSeenVersion) && VersionCompare.IsNewer(cur, _vm.LastSeenVersion))
        {
            var items = WhatsNewSince(_vm.LastSeenVersion);
            if (items.Count > 0) ShowWhatsNewDialog(cur, items);
        }
        _vm.LastSeenVersion = cur;   // remember this app version (for the next what's-new)
    }

    /// <summary>Curated registry of MEANINGFUL, workflow-affecting changes — one entry per spotlight-worthy
    /// feature, tagged with the version it shipped. RULE: only add an entry for a genuinely new button /
    /// feature / piece of functionality. Text tweaks, restyles, and bug fixes do NOT go here (release notes
    /// cover those) and must never trigger a spotlight. The post-update "What's new" tour shows the entries
    /// newer than the version last launched.</summary>
    private List<(string Version, string Summary, WalkStep Step)> FeatureSpotlights()
    {
        var L = Loc.Instance;
        return new()
        {
            ("0.3.18", L["Spot_318_Summary"],
                new WalkStep(null, () => _vm.IsPro ? ProToolsButton : GetProButton, L["Spot_318_Title"],
                    L["Spot_318_Body"])),
            ("0.3.20", L["Spot_320_Summary"],
                new WalkStep(0, () => MainTabs, L["Spot_320_Title"],
                    L["Spot_320_Body"])),
            ("0.3.22", L["Spot_322_Summary"],
                new WalkStep(null, () => SettingsButton, L["Spot_322_Title"],
                    L["Spot_322_Body"])),
            ("0.4.4", L["Spot_44_Summary"],
                new WalkStep(null, () => SettingsButton, L["Spot_44_Title"],
                    L["Spot_44_Body"])),
            ("0.4.8", L["Spot_48_Summary"],
                new WalkStep(1, () => BrowsePane, L["Spot_48_Title"],
                    _vm.IsPro ? L["Spot_48_Body_Pro"] : L["Spot_48_Body_Free"],
                    () => _vm.ShowDescribeDemo())),
            ("0.4.18", L["Spot_418_Summary"],
                new WalkStep(1, () => BrowsePane, L["Spot_418_Title"],
                    _vm.IsPro ? L["Spot_418_Body_Pro"] : L["Spot_418_Body_Free"])),
            ("1.0.3", L["Spot_103_Summary"],
                new WalkStep(0, () => UpdateAllButton, L["Spot_103_Title"],
                    L["Spot_103_Body"])),
        };
    }

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
        WalkStepCounter.Text = string.Format(Loc.Instance["Nav_StepOf"], _walkIndex + 1, _walk.Count);
        WalkTitle.Text = step.Title;
        WalkBody.Text = step.Body;
        WalkBack.Visibility = _walkIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        WalkSkip.Visibility = _walkIndex == _walk.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
        WalkNext.Content = _walkIndex == _walk.Count - 1 ? Loc.Instance["Nav_Done"] : Loc.Instance["Nav_Next"];

        if (step.Tab is int t && MainTabs.SelectedIndex != t) MainTabs.SelectedIndex = t;

        step.OnShow?.Invoke();   // optional scripted action for this step (e.g. the live "Describe" demo)

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
        _vm.ClearDescribeDemo();   // undo the scripted "Describe" demo if it ran, back to normal Browse
        if (_walkIsFirstRun && _vm.WalkthroughVersion < CurrentWalkthroughVersion)
            _vm.WalkthroughVersion = CurrentWalkthroughVersion;   // mark complete only on finish/skip
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
            Title = string.Format(Loc.Instance["Wn_Title"], version),
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
            Text = string.Format(Loc.Instance["Wn_Header"], version), Foreground = B("Text", "#E6E6E6"),
            FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(header, 0); grid.Children.Add(header);

        var notes = Loc.Instance["Wn_Intro"] + "\n\n"
                  + string.Join("\n", items.Select(i => "- " + i.Summary));
        var body = new Border
        {
            Background = B("Panel", "#252526"), BorderBrush = B("Border", "#3A3A3A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0),
            Child = BuildNotesView(notes, B),
        };
        Grid.SetRow(body, 1); grid.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var showMe = new Button { Content = Loc.Instance["Wn_ShowMe"], Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Primary") is Style ps) showMe.Style = ps;
        showMe.Click += (_, _) => { win.Close(); StartTour(items.Select(i => i.Step).ToList(), firstRun: false); };
        var close = new Button { Content = Loc.Instance["PT_Close"], IsCancel = true, IsDefault = true };
        if (TryFindResource("Ghost") is Style gs) close.Style = gs;
        close.Click += (_, _) => win.Close();
        buttons.Children.Add(showMe); buttons.Children.Add(close);
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons);

        win.Content = grid;
        win.ShowDialog();
    }

    // ==================== About dialog ====================

    private void ShowAboutDialog()
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = "About Shoyru's Addon Suite",
            Width = 420, SizeToContent = SizeToContent.Height,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(22) };

        panel.Children.Add(new TextBlock
        {
            Text = "Shoyru's Addon Suite", Foreground = B("Text", "#E6E6E6"),
            FontSize = 22, FontWeight = FontWeights.Bold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = _vm.AppVersion, Foreground = B("Muted", "#9A9AA6"),
            FontSize = 15, Margin = new Thickness(0, 2, 0, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.Instance["About_Tagline"],
            Foreground = B("Text", "#E6E6E6"), FontSize = 15, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = _vm.IsPro ? Loc.Instance["About_ProLicensed"] : Loc.Instance["About_FreeEdition"],
            Foreground = _vm.IsPro ? B("Accent", "#5B8DEF") : B("Muted", "#9A9AA6"),
            FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0),
        });

        void AddLink(string text, string url)
        {
            var link = new TextBlock { Margin = new Thickness(0, 8, 0, 0), FontSize = 14 };
            var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text))
            {
                Foreground = B("Accent", "#5B8DEF"),
            };
            h.Click += (_, _) => OpenUrl(url);
            link.Inlines.Add(h);
            panel.Children.Add(link);
        }
        AddLink(Loc.Instance["About_GitHub"], "https://github.com/shoyru-ai/eso-addon-manager");

        var close = new Button { Content = Loc.Instance["PT_Close"], IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        if (TryFindResource("Ghost") is Style gs) close.Style = gs;
        close.Click += (_, _) => win.Close();
        panel.Children.Add(close);

        win.Content = panel;
        win.ShowDialog();
    }

    // ==================== Terms & Conditions gate ====================

    /// <summary>Returns true if the current Terms are (or get) accepted; false if the user declines.</summary>
    private bool EnsureTermsAccepted()
    {
        if (_vm.AcceptedTermsVersion >= Services.Terms.CurrentVersion) return true;
        if (!ShowTermsDialog()) return false;
        _vm.AcceptedTermsVersion = Services.Terms.CurrentVersion;
        return true;
    }

    private bool ShowTermsDialog()
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = "Shoyru's Addon Suite — Terms & Conditions",
            Width = 580, Height = 620, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = Loc.Instance["Terms_Heading"], Foreground = B("Text", "#E6E6E6"),
            FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(header, 0); grid.Children.Add(header);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Background = B("Panel", "#252526"), BorderBrush = B("Border", "#3A3A3A"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(14),
                Child = new TextBlock
                {
                    Text = Services.Terms.Text, Foreground = B("Text", "#E6E6E6"),
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left, FontSize = 13, LineHeight = 19,
                },
            },
        };
        Grid.SetRow(scroller, 1); grid.Children.Add(scroller);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var decline = new Button { Content = Loc.Instance["Terms_Decline"], Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Ghost") is Style gs) decline.Style = gs;
        decline.Click += (_, _) => { win.DialogResult = false; };
        var agree = new Button { Content = Loc.Instance["Terms_Agree"] };
        if (TryFindResource("Primary") is Style ps) agree.Style = ps;
        agree.Click += (_, _) => { win.DialogResult = true; };
        buttons.Children.Add(decline); buttons.Children.Add(agree);
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons);

        win.Content = grid;
        return win.ShowDialog() == true;   // X / close = not accepted
    }

    // ==================== Full-size image viewer ====================

    private void DetailImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var urls = _vm.DetailImageUrls;
        if (urls.Count == 0 && !string.IsNullOrWhiteSpace(_vm.DetailImageUrl))
            urls = new List<string> { _vm.DetailImageUrl };
        ShowImageViewer(urls);
    }

    /// <summary>Opens the addon's screenshots full-size in a viewer. With more than one image, ‹ › buttons
    /// and the Left/Right arrow keys cycle through them (wrapping); Esc closes.</summary>
    private void ShowImageViewer(IReadOnlyList<string> urls)
    {
        if (urls is null || urls.Count == 0) return;
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var img = new System.Windows.Controls.Image
        {
            Stretch = System.Windows.Media.Stretch.Uniform,   // scale to fill the viewer, keep aspect
            Margin = new Thickness(16, 16, 16, 44),
        };
        var counter = new TextBlock
        {
            Foreground = B("Muted", "#9A9A9A"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12), FontSize = 13,
        };

        int index = 0;
        void Show(int i)
        {
            index = (i % urls.Count + urls.Count) % urls.Count;   // wrap both directions
            Behaviors.ImageLoader.SetSourceUrl(img, urls[index]);
            counter.Text = urls.Count > 1 ? $"{index + 1} / {urls.Count}    (← →)" : "";
        }

        Button Chevron(string glyph) => new()
        {
            Content = glyph, FontSize = 30, Width = 52, Height = 84, Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = B("Text", "#E6E6E6"), Background = B("PanelAlt", "#222227"), BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85,
        };
        var prev = Chevron("‹"); prev.HorizontalAlignment = HorizontalAlignment.Left; prev.Margin = new Thickness(8, 0, 0, 0);
        var next = Chevron("›"); next.HorizontalAlignment = HorizontalAlignment.Right; next.Margin = new Thickness(0, 0, 8, 0);
        prev.Click += (_, _) => Show(index - 1);
        next.Click += (_, _) => Show(index + 1);
        var multi = urls.Count > 1;
        prev.Visibility = next.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;

        var root = new Grid { Background = B("Bg", "#1E1E1E") };
        root.Children.Add(img);
        root.Children.Add(prev);
        root.Children.Add(next);
        root.Children.Add(counter);

        var wa = SystemParameters.WorkArea;
        var win = new Window
        {
            Title = string.IsNullOrWhiteSpace(_vm.DetailTitle) ? "Image" : _vm.DetailTitle,
            Owner = this,
            Width = Math.Min(1100, wa.Width * 0.9),
            Height = Math.Min(820, wa.Height * 0.9),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            ShowInTaskbar = false,
            Content = root,
        };
        win.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Left) Show(index - 1);
            else if (e.Key == System.Windows.Input.Key.Right) Show(index + 1);
            else if (e.Key == System.Windows.Input.Key.Escape) win.Close();
        };
        Show(0);
        win.ShowDialog();
    }

    private void Feedback_Click(object sender, RoutedEventArgs e) => ShowFeedbackDialog();

    /// <summary>Feedback modal. A non-null fixedType hides the type picker (used by the upgrade-intent and
    /// cancellation prompts). Auto-attaches version/tier/OS server-side.</summary>
    private void ShowFeedbackDialog(string? fixedType = null, string? heading = null, string? prompt = null)
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = Loc.Instance["Fb_Title"], Width = 480, MinWidth = 420, SizeToContent = SizeToContent.Height,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg", "#1E1E1E"), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = heading ?? Loc.Instance["Fb_Heading"], Foreground = B("Text", "#E6E6E6"), FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new TextBlock { Text = Loc.Instance["Fb_Sub"], Foreground = B("Muted", "#9A9A9A"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        System.Windows.Controls.ComboBox? typeCombo = null;
        if (fixedType is null)
        {
            panel.Children.Add(new TextBlock { Text = Loc.Instance["Fb_Type"], Foreground = B("Muted", "#9A9A9A"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            typeCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            // Localized labels shown to the user; the raw type sent to the backend stays English (see send handler).
            foreach (var t in new[] { Loc.Instance["Fb_Bug"], Loc.Instance["Fb_Idea"], Loc.Instance["Fb_Other"] }) typeCombo.Items.Add(t);
            typeCombo.SelectedIndex = 0;
            panel.Children.Add(typeCombo);
        }

        panel.Children.Add(new TextBlock { Text = prompt ?? Loc.Instance["Fb_Message"], Foreground = B("Muted", "#9A9A9A"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var msgBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(msgBox);

        panel.Children.Add(new TextBlock { Text = Loc.Instance["Fb_Contact"], Foreground = B("Muted", "#9A9A9A"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var contactBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(contactBox);

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(status);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var send = new Button { Content = Loc.Instance["Fb_Send"], Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Primary") is Style ps) send.Style = ps;
        var close = new Button { Content = Loc.Instance["PT_Close"], IsCancel = true };
        if (TryFindResource("Ghost") is Style gs) close.Style = gs;
        close.Click += (_, _) => win.Close();
        send.Click += async (_, _) =>
        {
            var msg = msgBox.Text.Trim();
            if (msg.Length == 0) { status.Text = Loc.Instance["Fb_EnterMessage"]; status.Foreground = B("Danger", "#E06C6C"); status.Visibility = Visibility.Visible; return; }
            send.IsEnabled = false;
            status.Text = Loc.Instance["Fb_Sending"]; status.Foreground = B("Muted", "#9A9A9A"); status.Visibility = Visibility.Visible;
            // Map the (possibly localized) picker selection back to a stable English type for the backend.
            var englishTypes = new[] { "Bug", "Idea", "Other" };
            var type = fixedType ?? (typeCombo is { SelectedIndex: >= 0 and var i } ? englishTypes[i] : "Other");
            var ok = await _vm.SendFeedbackAsync(type, msg, contactBox.Text.Trim());
            if (ok) { status.Text = Loc.Instance["Fb_Thanks"]; status.Foreground = B("Good", "#5BBF73"); send.Content = Loc.Instance["Fb_Sent"]; }
            else { status.Text = Loc.Instance["Fb_Failed"]; status.Foreground = B("Danger", "#E06C6C"); send.IsEnabled = true; }
        };
        row.Children.Add(send); row.Children.Add(close);
        panel.Children.Add(row);

        win.Content = panel;
        win.ShowDialog();
    }

    private void ShowLicenseDialog()
    {
        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var L = Loc.Instance;
        var win = new Window
        {
            Title = "Shoyru's Addon Suite - Pro",
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
        bool justActivatedPro = false;   // set on a successful free→Pro activation → triggers the Pro tour

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
                Text = L["Pro_ActiveHeader"], Foreground = B("Accent", "#5B8DEF"),
                FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(header);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = L["Pro_Unlock"], Foreground = B("Text", "#E6E6E6"),
                FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6),
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = _vm.IsPro ? L["Pro_ThanksSub"] : L["Pro_UnlockSub"],
            Foreground = B("Muted", "#9A9A9A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
        });

        // Free vs Pro comparison — the user's current tier is outlined/highlighted
        panel.Children.Add(BuildComparison(B));

        var status = new TextBlock { Foreground = B("Muted", "#9A9A9A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };

        if (!_vm.IsPro)
        {
            // Plan picker (dropdown) + anchored pricing — open the LS checkout for the chosen plan.
            var plans = _vm.ProPlans;
            if (BusinessConfig.Current.PurchaseEnabled && plans.Count > 0)
            {
                var combo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 10) };
                foreach (var p in plans)
                    combo.Items.Add(string.IsNullOrWhiteSpace(p.Badge) ? p.Name : $"{p.Name}  —  {p.Badge}");

                var priceLine = new TextBlock { Margin = new Thickness(0, 0, 0, 2) };
                var tagline = new TextBlock { Foreground = B("Muted", "#9A9A9A"), FontSize = 12, Margin = new Thickness(0, 0, 0, 12) };
                var getBtn = new Button { HorizontalAlignment = HorizontalAlignment.Left };
                if (TryFindResource("Update") is Style gs2) getBtn.Style = gs2;

                void Refresh()
                {
                    var p = plans[combo.SelectedIndex < 0 ? 0 : combo.SelectedIndex];
                    priceLine.Inlines.Clear();
                    if (!string.IsNullOrWhiteSpace(p.OriginalPrice))
                        priceLine.Inlines.Add(new System.Windows.Documents.Run(p.OriginalPrice + "   ")
                        { TextDecorations = TextDecorations.Strikethrough, Foreground = B("Muted", "#9A9A9A"), FontSize = 14 });
                    priceLine.Inlines.Add(new System.Windows.Documents.Run($"{p.Price} {p.Period}".Trim())
                    { FontWeight = FontWeights.Bold, FontSize = 19, Foreground = B("Text", "#E6E6E6") });
                    if (!string.IsNullOrWhiteSpace(p.Badge))
                        priceLine.Inlines.Add(new System.Windows.Documents.Run("    " + p.Badge)
                        { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = B("Accent", "#5B8DEF") });
                    tagline.Text = string.IsNullOrWhiteSpace(p.AvailabilityNote) ? p.Tagline
                        : string.IsNullOrWhiteSpace(p.Tagline) ? p.AvailabilityNote : $"{p.Tagline}  ·  {p.AvailabilityNote}";
                    getBtn.Content = p.TrialDays > 0 ? string.Format(L["Pro_StartTrial"], p.TrialDays) : (p.Recurring ? L["Pro_Subscribe"] : L["Pro_Buy"]);
                }
                combo.SelectionChanged += (_, _) => Refresh();
                combo.SelectedIndex = 0;   // first plan (Annual) is the default/nudge
                getBtn.Click += (_, _) => OpenUrl(plans[combo.SelectedIndex < 0 ? 0 : combo.SelectedIndex].CheckoutUrl);

                panel.Children.Add(combo);
                panel.Children.Add(priceLine);
                panel.Children.Add(tagline);
                panel.Children.Add(getBtn);
            }
            else
            {
                // Purchasing not live yet (store in review) — show a beta notice; key activation still works.
                panel.Children.Add(new TextBlock { Text = L["Pro_BetaSoftgate"], Foreground = B("Muted", "#9A9A9A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });
            }

            panel.Children.Add(new TextBlock { Text = L["Pro_HaveKey"], Foreground = B("Muted", "#9A9A9A"), FontSize = 13, Margin = new Thickness(0, 14, 0, 4) });
            var keyBox = new TextBox { FontSize = 15, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(keyBox);
            var activate = new Button { Content = L["Pro_Activate"], HorizontalAlignment = HorizontalAlignment.Left };
            if (TryFindResource("Primary") is Style ps) activate.Style = ps;
            activate.Click += async (_, _) =>
            {
                activate.IsEnabled = false;
                var msg = await _vm.ActivateLicenseAsync(keyBox.Text);
                status.Text = msg; status.Visibility = Visibility.Visible;
                status.Foreground = _vm.IsPro ? B("Good", "#5BBF73") : B("Danger", "#E06C6C");
                activate.IsEnabled = true;
                if (_vm.IsPro) { justActivatedPro = true; win.Close(); }   // unlocked — close; header shows the PRO badge
            };
            panel.Children.Add(activate);

            // Upgrade-intent capture: let undecided users tell us what's missing.
            var upsell = new TextBlock { Margin = new Thickness(0, 14, 0, 0) };
            var upLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(L["Pro_NotReady"])) { Foreground = B("Muted", "#9A9A9A") };
            upLink.Click += (_, _) => ShowFeedbackDialog("Upgrade", L["Pro_UpgradeFbTitle"], L["Pro_UpgradeFbPrompt"]);
            upsell.Inlines.Add(upLink);
            panel.Children.Add(upsell);
        }
        else
        {
            // Plan + renewal
            panel.Children.Add(new TextBlock { Text = string.Format(L["Pro_PlanLabel"], _vm.PlanName), Foreground = B("Text", "#E6E6E6"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            if (!string.IsNullOrWhiteSpace(_vm.RenewalText))
                panel.Children.Add(new TextBlock { Text = _vm.RenewalText, Foreground = B("Muted", "#9A9A9A"), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) });

            // Subscription management (lifetime licenses have nothing to manage)
            if (_vm.IsSubscription)
            {
                var subRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                var manage = new Button { Content = L["Pro_Manage"], Margin = new Thickness(0, 0, 8, 0) };
                if (TryFindResource("Primary") is Style ms) manage.Style = ms;
                manage.Click += async (_, _) => { await _vm.ManageSubscriptionAsync(); };
                var cancel = new Button { Content = L["Pro_Cancel"] };
                if (TryFindResource("Ghost") is Style cgs) cancel.Style = cgs;
                cancel.Click += async (_, _) =>
                {
                    if (MessageBox.Show(win, L["Pro_CancelConfirm"],
                        L["Pro_Cancel"], MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    cancel.IsEnabled = false;
                    var msg = await _vm.CancelSubscriptionAsync();
                    status.Text = msg; status.Visibility = Visibility.Visible;
                    status.Foreground = B("Text", "#E6E6E6");
                    cancel.IsEnabled = true;
                    // Churn insight: ask why (optional) right at the moment of cancellation.
                    if (_vm.CancellationSucceeded)
                        ShowFeedbackDialog("Cancellation", L["Pro_CancelFbTitle"], L["Pro_CancelFbPrompt"]);
                };
                subRow.Children.Add(manage); subRow.Children.Add(cancel);
                panel.Children.Add(subRow);
            }

            var removeRow = new StackPanel { Orientation = Orientation.Horizontal };
            var remove = new Button { Content = L["Pro_Remove"], Margin = new Thickness(0, 0, 8, 0) };
            if (TryFindResource("Ghost") is Style gs) remove.Style = gs;
            remove.Click += async (_, _) => { await _vm.RemoveLicenseAsync(); win.Close(); };
            var info = new Button { Content = L["Pro_WhatsThis"], VerticalAlignment = VerticalAlignment.Center };
            if (TryFindResource("Ghost") is Style gi) info.Style = gi;
            info.Click += (_, _) => MessageBox.Show(win, L["Pro_RemoveInfo"],
                L["Pro_Remove"], MessageBoxButton.OK, MessageBoxImage.Information);
            removeRow.Children.Add(remove);
            removeRow.Children.Add(info);
            panel.Children.Add(removeRow);
        }

        panel.Children.Add(status);

        // footer: support link
        var support = new TextBlock { Margin = new Thickness(0, 16, 0, 0) };
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(L["Pro_Support"])) { Foreground = B("Accent", "#3B82F6") };
        link.Click += (_, _) => OpenUrl(_vm.SupportUrl);
        support.Inlines.Add(link);
        panel.Children.Add(support);

        win.Content = panel;
        win.ShowDialog();

        // Just upgraded? Congratulate + tour the newly-unlocked Pro features (after the dialog closes).
        if (justActivatedPro) StartProTour();
    }

    /// <summary>Free vs Pro feature comparison; the user's current tier column is outlined + highlighted.</summary>
    private UIElement BuildComparison(Func<string, string, Brush> B)
    {
        var L = Loc.Instance;
        var rows = new (string icon, string color, string feat, bool free, bool pro)[]
        {
            ("🔎", "#5B8DEF", L["Cmp_Browse"],     true,  true),
            ("🗑", "#E06C6C", L["Cmp_Remove"],     true,  true),
            ("🔗", "#49C5A6", L["Cmp_Deps"],       true,  true),
            ("✨", "#A78BFA", L["Cmp_Describe"],   false, true),
            ("🌐", "#37C2C4", L["Cmp_Translate"],  false, true),
            ("🔄", "#5BBF73", L["Cmp_Update"],     true,  true),
            ("🧩", "#E0A458", L["Cmp_AutoDeps"],   false, true),
            ("🚀", "#6FA8FF", L["Cmp_AutoUpdate"], false, true),
            ("🎨", "#C77DFF", L["Cmp_Theme"],      false, true),
            ("🏷", "#E0B84E", L["Cmp_Categories"], false, true),
            ("📑", "#5BC8EF", L["Cmp_Profiles"],   false, true),
            ("💾", "#5BBF73", L["Cmp_Backups"],    false, true),
            ("☁", "#7DB8FF", L["Cmp_Sync"],       false, true),
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
                Text = you ? $"{t} {L["Cmp_You"]}" : t,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Bold, FontSize = 15,
                Foreground = you ? B("Accent", "#5B8DEF") : B("Text", "#E6E6E6"),
                Margin = new Thickness(0, 0, 0, 8),
            };
            return tb;
        }
        var featHdr = new TextBlock
        {
            Text = L["Cmp_Feature"], FontWeight = FontWeights.Bold, FontSize = 15, Foreground = B("Muted", "#9A9A9A"),
            Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(featHdr, 0); Grid.SetRow(featHdr, 0); grid.Children.Add(featHdr);
        var fh = Hdr(L["Cmp_Free"], yourCol == 1); Grid.SetColumn(fh, 1); Grid.SetRow(fh, 0); grid.Children.Add(fh);
        var ph = Hdr(L["Cmp_Pro"], yourCol == 2); Grid.SetColumn(ph, 2); Grid.SetRow(ph, 0); grid.Children.Add(ph);

        for (int i = 0; i < rows.Length; i++)
        {
            var (icon, color, feat, free, pro) = rows[i];
            var featPanel = new DockPanel { Margin = new Thickness(0, 4, 8, 4), LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
            var iconTb = new TextBlock
            {
                Text = icon, FontSize = 16, Width = 26,
                Foreground = (Brush)new BrushConverter().ConvertFromString(color)!,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(iconTb, Dock.Left);
            featPanel.Children.Add(iconTb);
            featPanel.Children.Add(new TextBlock
            {
                Text = feat, Foreground = B("Text", "#E6E6E6"), FontSize = 15, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(featPanel, 0); Grid.SetRow(featPanel, i + 1); grid.Children.Add(featPanel);

            TextBlock Mark(bool has) => new()
            {
                Text = has ? "✓" : "—", TextAlignment = TextAlignment.Center, FontSize = 17,
                FontWeight = has ? FontWeights.Bold : FontWeights.Normal,
                Foreground = has ? B("Good", "#5BBF73") : B("Muted", "#9A9A9A"),
                Margin = new Thickness(0, 4, 0, 4),
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
            Title = Loc.Instance["Pw_Title"],
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
            Text = Loc.Instance["Pw_Prompt"],
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
        var ok = new Button { Content = Loc.Instance["Pw_Unlock"], Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        if (TryFindResource("Primary") is Style ps) ok.Style = ps;
        var cancel = new Button { Content = Loc.Instance["Pw_Cancel"], IsCancel = true };
        if (TryFindResource("Ghost") is Style gs) cancel.Style = gs;
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3); grid.Children.Add(buttons);

        void Attempt()
        {
            if (_vm.CustomAddonsUnlocked) return;
            if (AccessGate.IsCorrect(pw.Password)) { _vm.CustomAddonsUnlocked = true; win.DialogResult = true; }
            else { error.Text = Loc.Instance["Pw_Incorrect"]; error.Visibility = Visibility.Visible; pw.Clear(); pw.Focus(); }
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
            ? Loc.Instance["Rn_NoNotes"]
            : _vm.AppUpdateNotes.Trim();

        Brush B(string key, string fallback) =>
            TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;

        var win = new Window
        {
            Title = string.Format(Loc.Instance["Rn_Title"], _vm.AppUpdateVersion),
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
            Text = string.Format(Loc.Instance["Rn_Header"], _vm.AppUpdateVersion),
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
        var updateBtn = new Button { Content = Loc.Instance["Rn_UpdateNow"], Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Update") is Style us) updateBtn.Style = us;
        updateBtn.Click += async (_, _) => { win.Close(); await _vm.DownloadAndApplyUpdateAsync(); };
        var closeBtn = new Button { Content = Loc.Instance["PT_Close"], IsCancel = true, IsDefault = true };
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

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
