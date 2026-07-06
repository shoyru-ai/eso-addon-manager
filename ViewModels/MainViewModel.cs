using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using EsoAddons.Models;
using EsoAddons.Mvvm;
using EsoAddons.Services;

namespace EsoAddons.ViewModels;

public enum AddonFilter { All, Addons, Libraries }
public enum BrowseSort { Downloads, Recent, Name }

public class MainViewModel : ObservableObject
{
    private readonly EsouiClient _client = new();
    private readonly AddonInstaller _installer;
    private readonly InstallStateStore _state = new();
    private readonly MyAddonsClient _myAddonsClient = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly LicenseService _license = new();
    private readonly LicenseStore _licenseStore = new();
    private LicenseInfo _licenseInfo = new();
    private IReadOnlyList<EsouiAddon> _catalog = Array.Empty<EsouiAddon>();
    private Dictionary<string, EsouiAddon> _catalogByDir = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, EsouiAddon> _catalogById = new();
    private readonly AddonFinderClient _finder = new();
    private readonly AddonTranslateClient _translator = new();
    private Dictionary<string, string> _categoryTitlesById = new();
    /// <summary>All addon/lib folder names present anywhere (top-level + bundled/nested) — used so a
    /// dependency satisfied by a bundled library isn't reported as missing.</summary>
    private HashSet<string> _availableAddonNames = new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _ppeChannel;
    private readonly AppUpdateService _appUpdate;

    /// <summary>Running app version (from the assembly), e.g. "v0.2.1" — shown in the header.
    /// Tagged [PPE] when on the staging/pre-release channel so it's obvious which channel you're testing.</summary>
    public string AppVersion => "v" + UpdateChecker.CurrentVersion + (_ppeChannel ? "  [PPE]" : "");

    private bool _customAddonsUnlocked;
    /// <summary>Whether the password gate on the Custom Addons tab has been passed this session.</summary>
    public bool CustomAddonsUnlocked
    {
        get => _customAddonsUnlocked;
        set => SetProperty(ref _customAddonsUnlocked, value);
    }

    // ---- Pro license ----
    private bool _isPro;
    /// <summary>True when a valid Pro license is active on this device (gates premium tool features).</summary>
    public bool IsPro
    {
        get => _isPro;
        private set { if (SetProperty(ref _isPro, value)) ApplyProState(); }
    }
    public bool IsNotPro => !IsPro;

    /// <summary>Refreshes Pro-gated UI after the license state changes. Updating addons is free,
    /// so it is no longer gated here — only dependency auto-install, categories, and theme are.</summary>
    private void ApplyProState()
    {
        OnPropertyChanged(nameof(IsNotPro));
        OnPropertyChanged(nameof(ShowDepsList));
        OnPropertyChanged(nameof(ShowDepsFreeNote));
        OnPropertyChanged(nameof(ShowCategoryEditor));
        InstalledView.Refresh();
    }

    // ---- theme (Pro: switch dark/light) ----
    public bool IsLightTheme => string.Equals(_settings.Theme, ThemeManager.Light, StringComparison.OrdinalIgnoreCase);
    public string ThemeToggleLabel => IsLightTheme ? Loc.Instance["Status_ThemeDark"] : Loc.Instance["Status_ThemeLight"];

    /// <summary>Toggle dark/light. Pro only. Persists + applies live.</summary>
    public void ToggleTheme()
    {
        if (!IsPro) return;
        _settings.Theme = IsLightTheme ? ThemeManager.Dark : ThemeManager.Light;
        _settingsStore.Save(_settings);
        ThemeManager.Apply(_settings.Theme);
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(ThemeToggleLabel));
    }

    // ---- dependency detail gating (Pro = full list + auto-install; free = a "requires X" note) ----
    public bool ShowDepsList => HasDependencies && IsPro;
    public bool ShowDepsFreeNote => HasDependencies && IsNotPro;
    private string _depFreeNote = "";
    public string DepFreeNote { get => _depFreeNote; set => SetProperty(ref _depFreeNote, value); }
    public string SupportUrl => BusinessConfig.Current.SupportUrl;
    public bool HasLicenseKey => _licenseInfo.HasKey;

    /// <summary>The Pro plans currently on offer (limited plans auto-drop after their AvailableUntil date).</summary>
    public List<PlanInfo> ProPlans => BusinessConfig.Current.Plans.Where(p => p.IsAvailable).ToList();

    private readonly FeedbackClient _feedback = new();
    /// <summary>Sends user feedback (auto-tagged with tier/version/OS). Returns true on success.</summary>
    public Task<bool> SendFeedbackAsync(string type, string message, string contact)
        => _feedback.SendAsync(type, message, contact, IsPro);

    // ---- current license details (for the Manage Pro plan/renewal line + cancel) ----
    private readonly SubscriptionBackend _backend = new();
    private string _licenseProductId = "";
    private string _licenseExpiresAtUtc = "";

    /// <summary>Friendly plan name for the active license, e.g. "Monthly"/"Annual"/"Lifetime".</summary>
    public string PlanName =>
        BusinessConfig.Current.PlanNamesByProductId.TryGetValue(_licenseProductId, out var n) ? n : "Pro";

    /// <summary>True when the active license is a subscription (has a period end) vs. a lifetime license.</summary>
    public bool IsSubscription => IsPro && !string.IsNullOrWhiteSpace(_licenseExpiresAtUtc);

    /// <summary>"Renews Jul 28, 2026" for subs, "Lifetime — never expires" otherwise.</summary>
    public string RenewalText
    {
        get
        {
            if (!IsPro) return "";
            if (string.IsNullOrWhiteSpace(_licenseExpiresAtUtc)) return Loc.Instance["Status_Lifetime"];
            return DateTimeOffset.TryParse(_licenseExpiresAtUtc, out var dt)
                ? string.Format(Loc.Instance["Status_ValidUntil"], dt.LocalDateTime.ToString("MMM d, yyyy"))
                : "";
        }
    }

    private void CaptureLicenseDetails(LicenseResult res)
    {
        _licenseProductId = res.ProductId;
        _licenseExpiresAtUtc = res.ExpiresAt;
        OnPropertyChanged(nameof(PlanName));
        OnPropertyChanged(nameof(IsSubscription));
        OnPropertyChanged(nameof(RenewalText));
    }

    /// <summary>Opens the customer's Lemon Squeezy portal (via the backend) to manage/cancel.</summary>
    public async Task ManageSubscriptionAsync()
    {
        if (!IsSubscription || !_licenseInfo.HasKey) return;
        Status = Loc.Instance["Status_OpeningPortal"];
        var url = await _backend.GetPortalUrlAsync(_licenseInfo.Key);
        if (string.IsNullOrEmpty(url)) { Status = Loc.Instance["Status_PortalFailed"]; return; }
        OpenUrl(url);
        Status = Loc.Instance["Status_PortalOpened"];
    }

    /// <summary>True when the most recent CancelSubscriptionAsync call succeeded — lets the UI trigger the
    /// churn-feedback prompt without sniffing the (now localized) status text.</summary>
    public bool CancellationSucceeded { get; private set; }

    /// <summary>Cancels the active subscription at period end (via the backend). Returns a status message.</summary>
    public async Task<string> CancelSubscriptionAsync()
    {
        CancellationSucceeded = false;
        if (!IsSubscription || !_licenseInfo.HasKey) return Loc.Instance["Status_NoSubToCancel"];
        Status = Loc.Instance["Status_Cancelling"];
        var (ok, endsAt, error) = await _backend.CancelAsync(_licenseInfo.Key);
        if (!ok) return Status = string.Format(Loc.Instance["Status_CouldntCancel"], error);
        var when = DateTimeOffset.TryParse(endsAt, out var dt) ? dt.LocalDateTime.ToString("MMM d, yyyy") : Loc.Instance["Status_BillingPeriodEnd"];
        CancellationSucceeded = true;
        return Status = string.Format(Loc.Instance["Status_SubCancelled"], when);
    }

    /// <summary>Validates the stored license on launch. Online check when possible; if offline, falls back to
    /// the last-known Pro state for a grace window so a dropped connection doesn't lock out a paying user.</summary>
    public async Task CheckLicenseAsync()
    {
        _licenseInfo = _licenseStore.Load();
        if (!_licenseInfo.HasKey) { IsPro = false; return; }

        // Offline founder license: always Pro, never phones Lemon Squeezy (so it survives the store going live).
        if (_licenseInfo.Founder) { IsPro = true; return; }

        // optimistic: show cached Pro immediately, then confirm online
        IsPro = _licenseInfo.ProCached;

        var res = await _license.ValidateAsync(_licenseInfo.Key, _licenseInfo.InstanceId);
        if (!string.IsNullOrEmpty(res.Error))
        {
            // offline / API unreachable -> 14-day grace on the last good validation
            IsPro = _licenseInfo.ProCached && (DateTime.UtcNow - _licenseInfo.LastValidatedUtc) < TimeSpan.FromDays(14);
            return;
        }

        IsPro = res.IsPro;
        CaptureLicenseDetails(res);
        _licenseInfo.ProCached = res.IsPro;
        _licenseInfo.LastValidatedUtc = DateTime.UtcNow;
        if (res.ProductId.Length > 0) _licenseInfo.ProductId = res.ProductId;
        _licenseStore.Save(_licenseInfo);
    }

    /// <summary>Activates a Pro key on this device. Returns a user-facing status message.</summary>
    public async Task<string> ActivateLicenseAsync(string key)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) return Loc.Instance["Status_EnterKey"];

        // Offline founder key (trusted beta access): grant Pro locally, no Lemon Squeezy. Survives the store
        // going live, so beta friends keep Pro for free, permanently.
        var founder = BusinessConfig.Current.FounderKey;
        if (founder.Length > 0 && string.Equals(key, founder, StringComparison.Ordinal))
        {
            _licenseInfo = new LicenseInfo { Key = key, Founder = true, ProCached = true, LastValidatedUtc = DateTime.UtcNow };
            _licenseStore.Save(_licenseInfo);
            IsPro = true;
            OnPropertyChanged(nameof(HasLicenseKey));
            return Status = "Founder access activated — Pro unlocked. Thank you! 💙";
        }

        Status = Loc.Instance["Status_Activating"];
        var res = await _license.ActivateAsync(key);

        if (!string.IsNullOrEmpty(res.Error))
            return Status = string.Format(Loc.Instance["Status_CouldntActivate"], res.Error);
        if (!res.Ok)
            return Status = Loc.Instance["Status_KeyDeviceLimit"];
        if (!LicenseService.ProductMatches(res.ProductId))
            return Status = Loc.Instance["Status_KeyWrongProduct"];
        if (res.Status != "active")
            return Status = string.Format(Loc.Instance["Status_KeyNotActive"], res.Status);

        _licenseInfo = new LicenseInfo
        {
            Key = key,
            InstanceId = res.InstanceId,
            ProductId = res.ProductId,
            ProCached = true,
            LastValidatedUtc = DateTime.UtcNow,
        };
        _licenseStore.Save(_licenseInfo);
        IsPro = true;
        CaptureLicenseDetails(res);
        OnPropertyChanged(nameof(HasLicenseKey));
        return Status = res.CustomerName.Length > 0 ? string.Format(Loc.Instance["Status_ProActivatedName"], res.CustomerName) : Loc.Instance["Status_ProActivated"];
    }

    /// <summary>Removes the license from this device (frees the seat).</summary>
    public async Task RemoveLicenseAsync()
    {
        if (_licenseInfo.HasKey)
            try { await _license.DeactivateAsync(_licenseInfo.Key, _licenseInfo.InstanceId); } catch { /* best effort */ }
        _licenseStore.Clear();
        _licenseInfo = new LicenseInfo();
        IsPro = false;
        OnPropertyChanged(nameof(HasLicenseKey));
        Status = Loc.Instance["Status_LicenseRemoved"];
    }

    public MainViewModel(string? addonsOverride = null, bool ppeChannel = false)
    {
        _ppeChannel = ppeChannel;
        _appUpdate = new AppUpdateService(ppeChannel);
        _installer = new AddonInstaller(_client);
        _settings = _settingsStore.Load();
        ThemeManager.Apply(_settings.Theme);   // apply saved theme before the UI renders
        // Apply saved language; if none chosen yet, default to the OS language when supported (the first-run
        // picker still appears so the user can confirm/change). Not persisted until the user picks.
        Loc.Instance.Lang = !string.IsNullOrEmpty(_settings.Language) ? _settings.Language
            : (Loc.Supported(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                ? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : "en");
        _status = Loc.Instance["Status_Ready"];   // now that the language is applied, in the chosen language
        if (!string.IsNullOrWhiteSpace(addonsOverride))
        {
            // Transient override from the --addons CLI flag (e.g. a clean sandbox folder). Not persisted.
            try { Directory.CreateDirectory(addonsOverride); } catch { /* best effort */ }
            AddonsPath = addonsOverride;
            AddonsFolderFound = Directory.Exists(addonsOverride);
        }
        else
        {
            var (path, found) = AddonsLocator.Resolve(_settings.AddonsPathOverride);
            AddonsPath = path;
            AddonsFolderFound = found;
        }

        InstalledView = CollectionViewSource.GetDefaultView(Installed);
        InstalledView.Filter = o => InstalledPasses((InstalledAddon)o);
        BrowseView = CollectionViewSource.GetDefaultView(SearchResults);
        BrowseView.Filter = o => PassesFilter(((EsouiAddon)o).IsLibrary);

        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(true));
        SearchCommand = new RelayCommand(_ => ApplyBrowse());
        DescribeCommand = new AsyncRelayCommand(_ => DescribeAsync());
        TranslateDetailCommand = new AsyncRelayCommand(_ => ToggleTranslateDetailAsync(), _ => CanTranslateDetail);
        SetSortCommand = new RelayCommand(s => Sort = Enum.Parse<BrowseSort>((string)s!));
        InstallMyAddonCommand = new AsyncRelayCommand(a => InstallMyAddonAsync((PublishedAddon)a!), a => a is PublishedAddon);
        RemoveMyAddonCommand = new AsyncRelayCommand(a => RemoveMyAddonAsync((PublishedAddon)a!), a => a is PublishedAddon);
        OpenMyAddonsRepoCommand = new RelayCommand(_ => OpenUrl("https://github.com/shoyru-ai/shoyru-eso-addons"));
        InstallCommand = new AsyncRelayCommand(a => InstallAsync((EsouiAddon)a!), a => a is EsouiAddon);
        RemoveBrowseCommand = new AsyncRelayCommand(a => RemoveBrowseAsync((EsouiAddon)a!), a => a is EsouiAddon);
        UpdateCommand = new AsyncRelayCommand(a => UpdateAsync((InstalledAddon)a!), a => a is InstalledAddon ia && ia.UpdateAvailable);
        RemoveCommand = new AsyncRelayCommand(a => RemoveAsync((InstalledAddon)a!), a => a is InstalledAddon);
        UpdateAllCommand = new AsyncRelayCommand(_ => UpdateAllAsync(), _ => UpdateCount > 0);
        OpenFolderCommand = new RelayCommand(_ => OpenUrl(AddonsPath));
        OpenPageCommand = new RelayCommand(_ => OpenUrl(DetailPageUrl), _ => !string.IsNullOrEmpty(DetailPageUrl));
        SetFilterCommand = new RelayCommand(f => Filter = Enum.Parse<AddonFilter>((string)f!));
        InstallDepCommand = new AsyncRelayCommand(a => InstallByDirAsync((string)a!), a => a is string s && _catalogByDir.ContainsKey(s));
    }

    private string _addonsPath = "";
    public string AddonsPath { get => _addonsPath; private set => SetProperty(ref _addonsPath, value); }

    private bool _addonsFolderFound = true;
    public bool AddonsFolderFound { get => _addonsFolderFound; set => SetProperty(ref _addonsFolderFound, value); }

    /// <summary>Raised when a failed install is confirmed (by a write probe) to be a folder-wide write
    /// block — ransomware protection / AV folder guard. The window shows the guided-fix dialog.</summary>
    public event Action? FolderAccessBlocked;
    private bool _lastFailureFolderBlocked;

    /// <summary>Actionable status-bar text for an install/update failure, plus diag logging.
    /// Raises <see cref="FolderAccessBlocked"/> when the whole AddOns folder rejects writes.</summary>
    private string DescribeInstallFailure(string what, Exception ex)
    {
        Diag.Log($"install failed ({what}) at {AddonsPath}: {ex}");
        _lastFailureFolderBlocked = false;
        switch (InstallFailure.Classify(ex))
        {
            case InstallFailure.Kind.CdnForbidden:
                return Loc.Instance["Err_CdnForbidden"];
            case InstallFailure.Kind.Network:
                return string.Format(Loc.Instance["Err_Network"], ex.Message);
            case InstallFailure.Kind.WriteBlocked when InstallFailure.ProbeWrite(AddonsPath) is { } probe:
                Diag.Log($"write probe blocked: {probe.Message}");
                _lastFailureFolderBlocked = true;
                FolderAccessBlocked?.Invoke();
                return Loc.Instance["Err_WriteBlocked"];
            default:
                return ex.Message;
        }
    }

    // ---- app self-update (Velopack) ----
    private bool _appUpdateAvailable;
    public bool AppUpdateAvailable { get => _appUpdateAvailable; set => SetProperty(ref _appUpdateAvailable, value); }
    private string _appUpdateVersion = "";
    public string AppUpdateVersion
    {
        get => _appUpdateVersion;
        set { if (SetProperty(ref _appUpdateVersion, value)) OnPropertyChanged(nameof(AppUpdateBannerText)); }
    }
    /// <summary>Localized one-line update-banner message (keeps the brand name English inside the sentence).</summary>
    public string AppUpdateBannerText => string.Format(Loc.Instance["Banner_NewVersion"], AppUpdateVersion);
    public string AppUpdateNotes { get; private set; } = "";

    private bool _isUpdating;
    /// <summary>True while the update is downloading (shows the progress bar, hides the buttons).</summary>
    public bool IsUpdating { get => _isUpdating; set => SetProperty(ref _isUpdating, value); }
    private int _updateProgress;
    /// <summary>Download progress 0-100 for the update banner's progress bar.</summary>
    public int UpdateProgress { get => _updateProgress; set => SetProperty(ref _updateProgress, value); }

    /// <summary>Checks GitHub Releases via Velopack; if a newer build exists, surfaces the update banner.
    /// No-op in dev / portable builds (only installed copies can self-update).</summary>
    public async Task CheckForAppUpdateAsync()
    {
        try
        {
            var info = await _appUpdate.CheckAsync();
            if (info is null) return;
            AppUpdateNotes = info.Notes;
            AppUpdateVersion = info.Version;
            AppUpdateAvailable = true;
            Status = string.Format(Loc.Instance["Status_NewVersion"], info.Version);
        }
        catch (Exception ex) { Diag.Log("update check failed: " + ex.Message); }
    }

    /// <summary>Downloads the pending update (showing progress) then applies it and restarts.</summary>
    public async Task DownloadAndApplyUpdateAsync()
    {
        if (!AppUpdateAvailable || IsUpdating) return;
        IsUpdating = true;
        UpdateProgress = 0;
        Status = string.Format(Loc.Instance["Status_Downloading"], AppUpdateVersion);
        try
        {
            await _appUpdate.DownloadAsync(p =>
                System.Windows.Application.Current?.Dispatcher.Invoke(() => UpdateProgress = p));
            Status = Loc.Instance["Status_Restarting"];
            _appUpdate.ApplyAndRestart();   // exits the process and relaunches the new version
        }
        catch (Exception ex)
        {
            IsUpdating = false;
            Status = string.Format(Loc.Instance["Status_UpdateFailed"], ex.Message);
        }
    }

    public ObservableCollection<InstalledAddon> Installed { get; } = new();
    public ObservableCollection<EsouiAddon> SearchResults { get; } = new();
    public ObservableCollection<PublishedAddon> MyAddons { get; } = new();
    public ICommand InstallMyAddonCommand { get; }
    public ICommand RemoveMyAddonCommand { get; }
    public ICommand OpenMyAddonsRepoCommand { get; }
    public ICollectionView InstalledView { get; }
    public ICollectionView BrowseView { get; }

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    /// <summary>AI "Describe" search — sends the search box text to the backend finder (Pro). See DescribeAsync.</summary>
    public ICommand DescribeCommand { get; }
    public ICommand TranslateDetailCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand RemoveBrowseCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenPageCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand InstallDepCommand { get; }
    public ICommand SetSortCommand { get; }

    // ---- browse categories + sort ----
    public ObservableCollection<Category> Categories { get; } = new();

    private Category? _selectedCategory;
    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) ApplyBrowse(); }
    }

    private BrowseSort _sort = BrowseSort.Downloads;
    public BrowseSort Sort
    {
        get => _sort;
        set { if (SetProperty(ref _sort, value)) { OnPropertyChanged(nameof(IsSortDownloads)); OnPropertyChanged(nameof(IsSortRecent)); OnPropertyChanged(nameof(IsSortName)); ApplyBrowse(); } }
    }
    public bool IsSortDownloads => Sort == BrowseSort.Downloads;
    public bool IsSortRecent => Sort == BrowseSort.Recent;
    public bool IsSortName => Sort == BrowseSort.Name;

    // ---- filter ----
    private AddonFilter _filter = AddonFilter.All;
    public AddonFilter Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) { OnPropertyChanged(nameof(IsAll)); OnPropertyChanged(nameof(IsAddons)); OnPropertyChanged(nameof(IsLibraries)); InstalledView.Refresh(); BrowseView.Refresh(); } }
    }
    public bool IsAll => Filter == AddonFilter.All;
    public bool IsAddons => Filter == AddonFilter.Addons;
    public bool IsLibraries => Filter == AddonFilter.Libraries;
    private bool PassesFilter(bool isLibrary) => Filter switch
    {
        AddonFilter.Addons => !isLibrary,
        AddonFilter.Libraries => isLibrary,
        _ => true,
    };

    /// <summary>Installed-tab row visibility: Addons/Libraries filter AND the live search text.</summary>
    private bool InstalledPasses(InstalledAddon a)
    {
        if (!PassesFilter(a.IsLibrary)) return false;
        var q = InstalledSearchText.Trim();
        if (q.Length == 0) return true;
        return a.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.FolderName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private string _installedSearchText = "";
    /// <summary>Installed-tab filter text. Setting it live-filters the installed list (no network).</summary>
    public string InstalledSearchText
    {
        get => _installedSearchText;
        set { if (SetProperty(ref _installedSearchText, value)) InstalledView.Refresh(); }
    }

    // ---- selection ----
    private InstalledAddon? _selectedInstalled;
    public InstalledAddon? SelectedInstalled
    {
        get => _selectedInstalled;
        set { if (SetProperty(ref _selectedInstalled, value) && value is not null) _ = ShowInstalledDetail(value); }
    }

    private EsouiAddon? _selectedBrowse;
    public EsouiAddon? SelectedBrowse
    {
        get => _selectedBrowse;
        set { if (SetProperty(ref _selectedBrowse, value) && value is not null) _ = ShowBrowseDetail(value); }
    }

    // ---- detail pane ----
    private bool _hasDetail;
    public bool HasDetail { get => _hasDetail; set { if (SetProperty(ref _hasDetail, value)) OnPropertyChanged(nameof(CanTranslateDetail)); } }
    private bool _detailIsInstalled;
    public bool DetailIsInstalled { get => _detailIsInstalled; set { if (SetProperty(ref _detailIsInstalled, value)) OnPropertyChanged(nameof(ShowCategoryEditor)); } }
    private string _detailTitle = "";
    public string DetailTitle { get => _detailTitle; set { if (SetProperty(ref _detailTitle, value)) ResetDetailTranslation(); } }
    private string _detailMeta = "";
    public string DetailMeta { get => _detailMeta; set => SetProperty(ref _detailMeta, value); }
    private string _detailImageUrl = "";
    public string DetailImageUrl { get => _detailImageUrl; set { if (SetProperty(ref _detailImageUrl, value)) OnPropertyChanged(nameof(HasDetailImage)); } }
    public bool HasDetailImage => !string.IsNullOrEmpty(DetailImageUrl);
    /// <summary>All full-size screenshots for the addon on show — the enlarge viewer cycles these.</summary>
    public List<string> DetailImageUrls { get; private set; } = new();
    private string _detailDescription = "";
    public string DetailDescription { get => _detailDescription; set { if (SetProperty(ref _detailDescription, value)) OnPropertyChanged(nameof(CanTranslateDetail)); } }
    private string _detailChangeLog = "";
    public string DetailChangeLog { get => _detailChangeLog; set { if (SetProperty(ref _detailChangeLog, value)) OnPropertyChanged(nameof(HasChangeLog)); } }
    public bool HasChangeLog => !string.IsNullOrWhiteSpace(DetailChangeLog);
    private string _detailPageUrl = "";
    public string DetailPageUrl { get => _detailPageUrl; set { if (SetProperty(ref _detailPageUrl, value)) OnPropertyChanged(nameof(HasDetailPage)); } }
    public bool HasDetailPage => !string.IsNullOrEmpty(DetailPageUrl);
    public ObservableCollection<DependencyStatus> DetailDependencies { get; } = new();
    private bool _hasDependencies;
    public bool HasDependencies { get => _hasDependencies; set { if (SetProperty(ref _hasDependencies, value)) { OnPropertyChanged(nameof(ShowDepsList)); OnPropertyChanged(nameof(ShowDepsFreeNote)); } } }
    private bool _showDepAutoNote;
    public bool ShowDepAutoNote { get => _showDepAutoNote; set => SetProperty(ref _showDepAutoNote, value); }
    private bool _showInstall;
    public bool ShowInstall { get => _showInstall; set => SetProperty(ref _showInstall, value); }
    private bool _browseDetailInstalled;
    /// <summary>True when the detail pane shows a Browse addon that's already installed (shows a badge instead of Install).</summary>
    public bool BrowseDetailInstalled { get => _browseDetailInstalled; set => SetProperty(ref _browseDetailInstalled, value); }
    private bool _showManage;
    public bool ShowManage { get => _showManage; set => SetProperty(ref _showManage, value); }
    private bool _detailUpdateAvailable;
    public bool DetailUpdateAvailable { get => _detailUpdateAvailable; set => SetProperty(ref _detailUpdateAvailable, value); }

    // detail-pane state for a selected "My Addon"
    private bool _showMyAddonActions;
    public bool ShowMyAddonActions { get => _showMyAddonActions; set => SetProperty(ref _showMyAddonActions, value); }
    private bool _myDetailShowInstall;
    public bool MyDetailShowInstall { get => _myDetailShowInstall; set => SetProperty(ref _myDetailShowInstall, value); }
    private bool _myDetailUpdateAvailable;
    public bool MyDetailUpdateAvailable { get => _myDetailUpdateAvailable; set => SetProperty(ref _myDetailUpdateAvailable, value); }
    private bool _myDetailShowRemove;
    public bool MyDetailShowRemove { get => _myDetailShowRemove; set => SetProperty(ref _myDetailShowRemove, value); }
    private PublishedAddon? _detailMyAddonTarget;
    public PublishedAddon? DetailMyAddonTarget { get => _detailMyAddonTarget; set => SetProperty(ref _detailMyAddonTarget, value); }

    private PublishedAddon? _selectedMyAddon;
    public PublishedAddon? SelectedMyAddon
    {
        get => _selectedMyAddon;
        set { if (SetProperty(ref _selectedMyAddon, value) && value is not null) ShowMyAddonDetail(value); }
    }

    // detail action targets
    private EsouiAddon? _detailInstallTarget;
    public EsouiAddon? DetailInstallTarget { get => _detailInstallTarget; set => SetProperty(ref _detailInstallTarget, value); }
    private InstalledAddon? _detailRemoveTarget;
    public InstalledAddon? DetailRemoveTarget { get => _detailRemoveTarget; set => SetProperty(ref _detailRemoveTarget, value); }

    private string _searchText = "";
    /// <summary>Browse filter text. Setting it live-filters the already-loaded catalog (no network call).</summary>
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyBrowse(); }
    }

    private string _status = Loc.Instance["Status_Ready"];
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    private int _updateCount;
    public int UpdateCount { get => _updateCount; set { if (SetProperty(ref _updateCount, value)) { OnPropertyChanged(nameof(UpdateSummary)); OnPropertyChanged(nameof(AnyUpdates)); } } }
    public string UpdateSummary => UpdateCount == 0 ? Loc.Instance["Status_AllUpToDate"] : string.Format(Loc.Instance["Status_UpdatesAvailable"], UpdateCount);
    /// <summary>True when at least one installed addon has an update available (drives the Update All button, free for everyone).</summary>
    public bool AnyUpdates => UpdateCount > 0;

    private int _missingDepsCount;
    public int MissingDepsCount { get => _missingDepsCount; set { if (SetProperty(ref _missingDepsCount, value)) { OnPropertyChanged(nameof(AnyMissingDeps)); OnPropertyChanged(nameof(MissingDepsSummary)); } } }
    public bool AnyMissingDeps => MissingDepsCount > 0;
    public string MissingDepsSummary => MissingDepsCount == 1
        ? Loc.Instance["Status_OneMissingDep"]
        : string.Format(Loc.Instance["Status_ManyMissingDeps"], MissingDepsCount);

    // ---- load ----
    public async Task LoadAsync(bool refreshCatalog = false)
    {
        try
        {
            IsBusy = true;
            Status = Loc.Instance["Status_FetchingCatalog"];
            _catalog = await _client.GetCatalogAsync(refreshCatalog);
            _catalogByDir = new Dictionary<string, EsouiAddon>(StringComparer.OrdinalIgnoreCase);
            _catalogById = new Dictionary<string, EsouiAddon>();
            foreach (var c in _catalog)
            {
                _catalogById[c.Id] = c;                            // for mapping AI-find results back to catalog entries
                foreach (var dir in c.Dirs)
                    if (!_catalogByDir.TryGetValue(dir, out var existing) || c.Downloads > existing.Downloads)
                        _catalogByDir[dir] = c;
            }

            RescanInstalled();

            // categories (buckets) — derive from catalog so only non-empty ones show
            try
            {
                var cats = await _client.GetCategoriesAsync();
                _categoryTitlesById = cats.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Title);
                var counts = _catalog.GroupBy(a => a.CategoryId).ToDictionary(g => g.Key, g => g.Count());
                Categories.Clear();
                Categories.Add(new Category { Id = "", Title = Loc.Instance["Status_AllCategories"] });
                foreach (var kv in counts.Where(k => _categoryTitlesById.ContainsKey(k.Key)).OrderBy(k => _categoryTitlesById[k.Key]))
                    Categories.Add(new Category { Id = kv.Key, Title = _categoryTitlesById[kv.Key], Count = kv.Value });
                _selectedCategory = Categories[0];
                OnPropertyChanged(nameof(SelectedCategory));
                ApplyEsouiCategories();   // now that titles are known, fill the Installed Category column
            }
            catch { /* categories are optional; browse still works */ }

            ApplyBrowse(); // populate Browse by default (top downloads) so it's never blank
            await LoadMyAddonsAsync();
            Status = string.Format(Loc.Instance["Status_InstalledCatalog"], Installed.Count, _catalog.Count);
        }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_Error"], ex.Message); }
        finally { IsBusy = false; }
    }

    private void RescanInstalled()
    {
        var prevSelected = _selectedInstalled?.FolderName;
        var scanned = AddonScanner.Scan(AddonsPath);
        Installed.Clear();
        foreach (var a in scanned)
        {
            if (_catalogByDir.TryGetValue(a.FolderName, out var match))
            {
                a.EsouiId = match.Id;
                a.LatestVersion = match.Version;
                a.LastUpdateMs = match.LastUpdateMs;
                a.EsouiCategory = _categoryTitlesById.TryGetValue(match.CategoryId, out var ct) ? ct : "";
                a.ThumbUrl = match.ThumbUrl;
                a.ImageUrl = match.ImageUrl;
                a.ImageUrls = match.ImageUrls;
                if (match.IsLibrary) a.IsLibrary = true;
                var recorded = _state.Get(a.FolderName);
                if (recorded is not null) a.RecordedVersion = recorded;
            }
            Installed.Add(a);
        }
        RecountUpdates();
        // Available = top-level AND bundled/nested libs, so a dependency satisfied by a bundled library
        // isn't flagged missing. Only flag deps that are missing AND obtainable on ESOUI (actionable).
        _availableAddonNames = AddonScanner.AllAddonFolderNames(AddonsPath);
        foreach (var a in Installed)
        {
            a.MissingDeps = string.Join(", ", a.Dependencies.Where(d =>
                !_availableAddonNames.Contains(d) && _catalogByDir.ContainsKey(d)));   // actionable health check
            a.UserCategory = _settings.InstalledCategories.TryGetValue(a.FolderName, out var cat) ? cat : "";
        }
        MissingDepsCount = Installed.Count(a => a.HasMissingDeps);
        RefreshKnownCategories();
        InstalledView.Refresh();

        // Preserve the detail selection across the rescan so the detail pane — and its
        // dependency ✓/✗ list + Get buttons — refreshes live after an install/update.
        if (prevSelected is not null)
        {
            var again = Installed.FirstOrDefault(a => a.FolderName.Equals(prevSelected, StringComparison.OrdinalIgnoreCase));
            if (again is not null)
            {
                _selectedInstalled = again;
                OnPropertyChanged(nameof(SelectedInstalled));
                DetailRemoveTarget = again;
                if (DetailIsInstalled) BuildDependencyList(again);
            }
        }
        RefreshMyAddonStatus();
    }

    // ---- Shoyru's published addons ----
    public async Task LoadMyAddonsAsync()
    {
        var list = await _myAddonsClient.GetAsync();
        MyAddons.Clear();
        foreach (var p in list) MyAddons.Add(p);
        RefreshMyAddonStatus();
    }

    private void RefreshMyAddonStatus()
    {
        foreach (var p in MyAddons)
        {
            var inst = Installed.FirstOrDefault(i => i.FolderName.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            p.IsInstalled = inst is not null;
            p.InstalledVersion = inst?.Version ?? "";
        }
    }

    private async Task InstallMyAddonAsync(PublishedAddon p)
    {
        try
        {
            p.Status = Loc.Instance["Status_InstallingItem"];
            Status = string.Format(Loc.Instance["Status_InstallingTitle"], p.Title);
            await _installer.InstallFromUrlAsync(p.DownloadUrl, AddonsPath);
            _state.Set(p.Name, p.Version); _state.Save();
            RescanInstalled();
            // Auto-install required libraries (## DependsOn) — a PRO feature. Free users see the dependency
            // named in the detail pane and install it themselves.
            var deps = IsPro ? await EnsureDependenciesForAsync(new[] { p.Name }) : 0;
            RefreshMyAddonStatus();
            p.Status = "";
            Status = deps > 0 ? string.Format(Loc.Instance["Status_InstalledLibs"], p.Title, deps) : string.Format(Loc.Instance["Status_InstalledTitle"], p.Title);
        }
        catch (Exception ex) { p.Status = Loc.Instance["Status_ItemFailed"]; Status = string.Format(Loc.Instance["Status_InstallFailed"], DescribeInstallFailure(p.Title, ex)); }
    }

    private Task RemoveMyAddonAsync(PublishedAddon p)
    {
        var inst = Installed.FirstOrDefault(i => i.FolderName.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
        if (inst is null) { Status = string.Format(Loc.Instance["Status_NotInFolder"], p.Title); return Task.CompletedTask; }
        var (ok, msg) = AddonInstaller.Uninstall(inst);
        Status = msg;
        if (ok)
        {
            RescanInstalled();
            RefreshMyAddonStatus();
            ApplyBrowse();
            // refresh the detail pane if this addon is the one on show
            if (ReferenceEquals(_selectedMyAddon, p) || ReferenceEquals(_detailMyAddonTarget, p)) ShowMyAddonDetail(p);
        }
        return Task.CompletedTask;
    }

    /// <summary>Resolves the installed addon(s) that a Browse catalog entry corresponds to —
    /// by ESOUI id first, then by the folder names it installs.</summary>
    private List<InstalledAddon> FindInstalledFor(EsouiAddon a)
    {
        var byId = Installed.Where(i => i.Managed && i.EsouiId == a.Id).ToList();
        if (byId.Count > 0) return byId;
        return Installed.Where(i => a.Dirs.Any(d => d.Equals(i.FolderName, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Removes an addon from the Browse/search tab when it's already installed.</summary>
    private Task RemoveBrowseAsync(EsouiAddon a)
    {
        var targets = FindInstalledFor(a);
        if (targets.Count == 0) { Status = string.Format(Loc.Instance["Status_NotInstalled"], a.Title); return Task.CompletedTask; }

        int removed = 0; string lastMsg = "";
        foreach (var t in targets)
        {
            var (ok, msg) = AddonInstaller.Uninstall(t);
            lastMsg = msg;
            if (ok) removed++;
        }

        if (removed > 0)
        {
            a.IsInstalled = false;
            if (ReferenceEquals(_selectedBrowse, a)) { ShowInstall = true; BrowseDetailInstalled = false; }
            RescanInstalled();
            RefreshMyAddonStatus();
            ApplyBrowse();
            Status = removed == 1 ? lastMsg : string.Format(Loc.Instance["Status_RemovedFolders"], removed, a.Title);
        }
        else Status = lastMsg;
        return Task.CompletedTask;
    }

    /// <summary>Populates the Browse list from the catalog using the current category, search text, and sort.</summary>
    private void ApplyBrowse()
    {
        SearchResults.Clear();
        if (_catalog.Count == 0) return;

        var q = SearchText.Trim();
        IEnumerable<EsouiAddon> items = _catalog;

        if (SelectedCategory is { Id.Length: > 0 } cat)
            items = items.Where(a => a.CategoryId == cat.Id);

        if (!string.IsNullOrWhiteSpace(q))
            items = items.Where(a => a.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || a.Author.Contains(q, StringComparison.OrdinalIgnoreCase));

        items = Sort switch
        {
            BrowseSort.Recent => items.OrderByDescending(a => a.LastUpdateMs),
            BrowseSort.Name => items.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderByDescending(a => a.Downloads),
        };

        var installedIds = new HashSet<string>(Installed.Where(i => i.Managed).Select(i => i.EsouiId));
        foreach (var a in items.Take(300))
        {
            a.IsInstalled = installedIds.Contains(a.Id);
            a.MatchReason = "";   // normal browsing → drop any leftover AI-find reason on this reused entry
            SearchResults.Add(a);
        }
        BrowseView.Refresh();
        var where = SelectedCategory is { Id.Length: > 0 } c ? $" · {c.Title}" : "";
        Status = string.Format(Loc.Instance["Status_ShowingAddons"], SearchResults.Count, where);
    }

    // ---- AI "Describe" search (Pro) ----
    private bool _isFinding;
    /// <summary>True while an AI Describe request is in flight (drives a spinner / disables the button).</summary>
    public bool IsFinding { get => _isFinding; set => SetProperty(ref _isFinding, value); }

    /// <summary>Takes the Browse search box text as a natural-language description, asks the backend finder for
    /// matching add-ons, and shows them in the Browse list (each tagged with a one-line reason). Pro-only; the
    /// backend re-checks the license before spending any model tokens. A plain "Search" still does the instant
    /// local name/author filter — this is the same box, used a different way.</summary>
    public async Task DescribeAsync()
    {
        var q = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(q) || IsFinding || !IsPro) return;   // UI also gates to Pro
        IsFinding = true;
        Status = string.Format(Loc.Instance["Status_Finding"], q);
        try
        {
            var result = await _finder.FindAsync(q, _licenseInfo.Key);
            var installedIds = new HashSet<string>(Installed.Where(i => i.Managed).Select(i => i.EsouiId));
            SearchResults.Clear();
            foreach (var m in result.Matches)
            {
                if (_catalogById.TryGetValue(m.Id, out var a))
                {
                    a.MatchReason = m.Reason;
                    a.IsInstalled = installedIds.Contains(a.Id);
                    SearchResults.Add(a);
                }
            }
            BrowseView.Refresh();
            Status = result.Error is not null
                ? result.Error                                    // e.g. "Daily search limit reached (50/day)…"
                : SearchResults.Count > 0
                    ? string.Format(Loc.Instance["Status_Matches"], SearchResults.Count, q)
                    : AddonFinderClient.IsConfigured
                        ? Loc.Instance["Status_NoMatches"]
                        : Loc.Instance["Status_DescribeUnavailable"];
        }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_FindFailed"], ex.Message); }
        finally { IsFinding = false; }
    }

    // ---- On-demand add-on description translation (Pro) ----
    private string? _origDescription;   // holds the source text while a translation is displayed
    private bool _isTranslatingDetail;
    /// <summary>True while a translate request is in flight (spinner / disables the button).</summary>
    public bool IsTranslatingDetail { get => _isTranslatingDetail; set { if (SetProperty(ref _isTranslatingDetail, value)) OnPropertyChanged(nameof(TranslateLabel)); } }
    public bool IsDetailTranslated => _origDescription != null;
    /// <summary>Show the 🌐 Translate button only for Pro users, with a backend configured and a description present.</summary>
    public bool CanTranslateDetail => AddonTranslateClient.IsConfigured && IsPro && HasDetail && !string.IsNullOrWhiteSpace(DetailDescription);
    public string TranslateLabel => IsTranslatingDetail ? Loc.Instance["Btn_Translating"]
        : IsDetailTranslated ? Loc.Instance["Btn_ShowOriginal"] : Loc.Instance["Btn_Translate"];

    /// <summary>Reset translation state when a different add-on is shown (called from the DetailTitle setter).</summary>
    private void ResetDetailTranslation()
    {
        _origDescription = null;
        OnPropertyChanged(nameof(IsDetailTranslated));
        OnPropertyChanged(nameof(TranslateLabel));
        OnPropertyChanged(nameof(CanTranslateDetail));
    }

    /// <summary>Toggle the detail description between its original text and a machine translation into the current
    /// UI language (DeepL via the backend). Pro-gated + daily-capped server-side; surfaces cap/quota messages.</summary>
    private async Task ToggleTranslateDetailAsync()
    {
        if (_origDescription != null)   // currently showing a translation → restore original
        {
            DetailDescription = _origDescription;
            _origDescription = null;
            OnPropertyChanged(nameof(IsDetailTranslated));
            OnPropertyChanged(nameof(TranslateLabel));
            return;
        }
        var text = DetailDescription;
        if (string.IsNullOrWhiteSpace(text) || IsTranslatingDetail) return;
        IsTranslatingDetail = true;
        try
        {
            var res = await _translator.TranslateAsync(text, Loc.Instance.Lang, _licenseInfo.Key);
            if (!string.IsNullOrEmpty(res.Text))
            {
                _origDescription = text;
                DetailDescription = res.Text!;
                OnPropertyChanged(nameof(IsDetailTranslated));
                OnPropertyChanged(nameof(TranslateLabel));
            }
            else if (!string.IsNullOrEmpty(res.Error))
                Status = res.Error;   // e.g. daily cap / monthly quota reached
        }
        finally { IsTranslatingDetail = false; }
    }

    /// <summary>Walkthrough demo: scripts the "Describe" flow with a canned result so new users see it work —
    /// no backend call, no tokens, and it works for free users too. Picks real, popular catalog add-ons so
    /// something genuinely pops up; the reason text is labelled as an example.</summary>
    public void ShowDescribeDemo()
    {
        if (_catalog.Count == 0) return;
        Filter = AddonFilter.All;                 // don't let the Addons/Libraries filter hide the demo result
        _searchText = "show me alerts for abilities";
        OnPropertyChanged(nameof(SearchText));    // show the example in the box WITHOUT running the local filter

        SearchResults.Clear();
        // Prefer real alert/notification add-ons so the example reads true; fall back to popular ones.
        static bool IsAlertish(EsouiAddon a) =>
            new[] { "alert", "alarm", "warn", "notif", "proc", "reminder", "cooldown", "timer" }
                .Any(k => a.Title.Contains(k, StringComparison.OrdinalIgnoreCase));
        var picks = _catalog.Where(IsAlertish).OrderByDescending(a => a.Downloads).Take(6).ToList();
        if (picks.Count == 0)
            picks = _catalog.OrderByDescending(a => a.Downloads).Take(6).ToList();

        foreach (var a in picks)
        {
            a.MatchReason = "";        // reason-less: the result IS the list of similar add-ons
            a.IsInstalled = false;
            SearchResults.Add(a);
        }
        BrowseView.Refresh();
        Status = Loc.Instance["Status_DescribeExample"];
    }

    /// <summary>Undo the walkthrough demo (clear the example text + results back to normal Browse).</summary>
    public void ClearDescribeDemo()
    {
        if (SearchText.Length > 0) SearchText = "";   // setter runs ApplyBrowse → restores Browse + clears reasons
        else ApplyBrowse();
    }

    // ---- detail population ----
    private async Task ShowInstalledDetail(InstalledAddon a)
    {
        HasDetail = true;
        DetailIsInstalled = true;
        ShowInstall = false;
        BrowseDetailInstalled = false;
        ShowManage = true;
        ShowMyAddonActions = false;
        MyDetailShowInstall = false; MyDetailUpdateAvailable = false; MyDetailShowRemove = false;
        DetailUpdateAvailable = a.UpdateAvailable;   // updating addons is free
        DetailInstallTarget = null;
        DetailRemoveTarget = a;
        DetailTitle = a.Title;
        DetailMeta = a.Managed
            ? string.Format(Loc.Instance["Status_MetaInstalledManaged"], a.Version, a.LatestVersion, a.Author)
            : string.Format(Loc.Instance["Status_MetaInstalledUnmanaged"], a.Version, a.Author);
        DetailImageUrl = !string.IsNullOrWhiteSpace(a.ImageUrl) ? a.ImageUrl : a.ThumbUrl;
        DetailImageUrls = a.ImageUrls;
        DetailDescription = a.Description;
        DetailChangeLog = "";
        DetailPageUrl = "";
        ShowDepAutoNote = false;
        DetailCategoryInput = a.UserCategory;
        DetailCategoryHint = a.EsouiCategory.Length > 0
            ? string.Format(Loc.Instance["Status_CategoryHintAuto"], a.EsouiCategory)
            : Loc.Instance["Status_CategoryHintNone"];
        OnPropertyChanged(nameof(ShowCategoryEditor));
        BuildDependencyList(a);

        if (a.Managed)
        {
            if (_catalogByDir.TryGetValue(a.FolderName, out var cat)) DetailPageUrl = cat.FileInfoUri;
            try
            {
                var d = await _client.GetDetailsAsync(a.EsouiId);
                if (ReferenceEquals(_selectedInstalled, a))
                {
                    if (!string.IsNullOrWhiteSpace(d.ImageUrl)) DetailImageUrl = d.ImageUrl;
                    if (!string.IsNullOrWhiteSpace(d.Description)) DetailDescription = d.Description;
                    DetailChangeLog = d.ChangeLog;
                }
            }
            catch { /* keep manifest description */ }
        }
    }

    private async Task ShowBrowseDetail(EsouiAddon a)
    {
        HasDetail = true;
        DetailIsInstalled = false;
        ShowInstall = !a.IsInstalled;
        BrowseDetailInstalled = a.IsInstalled;
        ShowManage = false;
        ShowMyAddonActions = false;
        MyDetailShowInstall = false; MyDetailUpdateAvailable = false; MyDetailShowRemove = false;
        DetailUpdateAvailable = false;
        DetailRemoveTarget = null;
        DetailInstallTarget = a;
        DetailTitle = a.Title;
        DetailMeta = string.Format(Loc.Instance["Status_MetaBrowse"], a.Version, a.DownloadsDisplay, a.LastUpdated, a.Author);
        DetailImageUrl = !string.IsNullOrWhiteSpace(a.ImageUrl) ? a.ImageUrl : a.ThumbUrl;
        DetailImageUrls = a.ImageUrls;
        DetailPageUrl = a.FileInfoUri;
        DetailDescription = Loc.Instance["Status_LoadingDesc"];
        DetailChangeLog = "";
        DetailDependencies.Clear();
        HasDependencies = false;
        ShowDepAutoNote = true;   // browse: ESOUI API doesn't expose deps; they auto-install

        try
        {
            var d = await _client.GetDetailsAsync(a.Id);
            if (ReferenceEquals(_selectedBrowse, a))
            {
                if (!string.IsNullOrWhiteSpace(d.ImageUrl)) DetailImageUrl = d.ImageUrl;
                DetailDescription = string.IsNullOrWhiteSpace(d.Description) ? Loc.Instance["Status_NoDescription"] : d.Description;
                DetailChangeLog = d.ChangeLog;
            }
        }
        catch (Exception ex) { DetailDescription = string.Format(Loc.Instance["Status_LoadDescFailed"], ex.Message); }
    }

    private void ShowMyAddonDetail(PublishedAddon p)
    {
        HasDetail = true;
        DetailTitle = p.Title;
        DetailMeta = p.StatusLabel;
        DetailImageUrl = "";
        DetailImageUrls = new();
        DetailDescription = string.IsNullOrWhiteSpace(p.Description) ? Loc.Instance["Status_NoDescriptionProvided"] : p.Description;
        DetailChangeLog = "";
        DetailPageUrl = "";
        // Show the addon's required libraries (from the manifest) so the user knows what's needed
        // before installing — they install automatically, but this makes them visible.
        DetailDependencies.Clear();
        var installed = _availableAddonNames;   // top-level + bundled/nested libs
        foreach (var d in p.Dependencies)
            DetailDependencies.Add(new DependencyStatus { Name = d, IsInstalled = installed.Contains(d), IsOptional = false, IsGettable = _catalogByDir.ContainsKey(d) });
        HasDependencies = DetailDependencies.Count > 0;
        SetDepFreeNote();
        ShowDepAutoNote = false;
        // hide installed/browse buttons, show My-Addon actions
        DetailIsInstalled = false;
        ShowInstall = false;
        BrowseDetailInstalled = false;
        ShowManage = false;
        DetailUpdateAvailable = false;
        ShowMyAddonActions = true;
        DetailMyAddonTarget = p;
        MyDetailShowInstall = p.ShowInstall;
        MyDetailUpdateAvailable = p.UpdateAvailable;   // updating addons is free
        MyDetailShowRemove = p.ShowRemove;
    }

    private void BuildDependencyList(InstalledAddon a)
    {
        DetailDependencies.Clear();
        var installed = _availableAddonNames;   // top-level + bundled/nested libs
        foreach (var d in a.Dependencies)
            DetailDependencies.Add(new DependencyStatus { Name = d, IsInstalled = installed.Contains(d), IsOptional = false, IsGettable = _catalogByDir.ContainsKey(d) });
        foreach (var d in a.OptionalDependencies)
            DetailDependencies.Add(new DependencyStatus { Name = d, IsInstalled = installed.Contains(d), IsOptional = true, IsGettable = _catalogByDir.ContainsKey(d) });
        HasDependencies = DetailDependencies.Count > 0;
        SetDepFreeNote();
    }

    /// <summary>Builds the free-tier "Requires: X, Y. Get Pro to install dependencies automatically." note.</summary>
    private void SetDepFreeNote()
    {
        var required = DetailDependencies.Where(d => !d.IsOptional).Select(d => d.Name).ToList();
        DepFreeNote = required.Count > 0
            ? string.Format(Loc.Instance["Status_RequiresNote"], string.Join(", ", required))
            : "";
    }

    // ---- actions ----
    private async Task InstallAsync(EsouiAddon addon)
    {
        try
        {
            addon.Status = Loc.Instance["Status_InstallingItem"];
            Status = string.Format(Loc.Instance["Status_InstallingTitle"], addon.Title);
            await _installer.InstallAsync(addon.Id, AddonsPath);
            RecordInstalled(addon);
            RescanInstalled();
            var deps = IsPro ? await EnsureDependenciesForAsync(addon.Dirs) : 0;   // auto-deps = Pro
            addon.IsInstalled = true;
            addon.Status = Loc.Instance["Status_ItemInstalled"];
            if (ReferenceEquals(_selectedBrowse, addon)) { ShowInstall = false; BrowseDetailInstalled = true; }
            ApplyBrowse();
            Status = deps > 0 ? string.Format(Loc.Instance["Status_InstalledDeps"], addon.Title, deps) : string.Format(Loc.Instance["Status_InstalledTitle"], addon.Title);
        }
        catch (Exception ex) { addon.Status = Loc.Instance["Status_ItemFailed"]; Status = string.Format(Loc.Instance["Status_InstallFailed"], DescribeInstallFailure(addon.Title, ex)); }
    }

    private async Task InstallByDirAsync(string dir)
    {
        if (!_catalogByDir.TryGetValue(dir, out var cat)) { Status = string.Format(Loc.Instance["Status_DirNotFound"], dir); return; }
        Status = string.Format(Loc.Instance["Status_InstallingTitle"], cat.Title);
        try
        {
            await _installer.InstallAsync(cat.Id, AddonsPath);
            RecordInstalled(cat, dir);
            RescanInstalled(); // restores selection + refreshes the dependency ✓/✗ list
            Status = string.Format(Loc.Instance["Status_InstalledTitle"], cat.Title);
        }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_InstallFailed"], DescribeInstallFailure(cat.Title, ex)); }
    }

    private async Task UpdateAsync(InstalledAddon addon)
    {
        try
        {
            addon.Status = Loc.Instance["Status_UpdatingItem"];
            Status = string.Format(Loc.Instance["Status_UpdatingTitle"], addon.Title);
            await _installer.InstallAsync(addon.EsouiId, AddonsPath);
            if (_catalogByDir.TryGetValue(addon.FolderName, out var cat)) RecordInstalled(cat, addon.FolderName);
            RescanInstalled();
            if (IsPro) await EnsureDependenciesForAsync(new[] { addon.FolderName });   // auto-deps = Pro
            Status = string.Format(Loc.Instance["Status_UpdatedTitle"], addon.Title);
        }
        catch (Exception ex) { addon.Status = Loc.Instance["Status_ItemFailed"]; Status = string.Format(Loc.Instance["Status_UpdateFailed"], DescribeInstallFailure(addon.Title, ex)); }
    }

    private async Task UpdateAllAsync()
    {
        var outdated = Installed.Where(a => a.UpdateAvailable).ToList();
        int done = 0;
        foreach (var a in outdated)
        {
            try
            {
                Status = string.Format(Loc.Instance["Status_UpdatingProgress"], a.Title, done + 1, outdated.Count);
                await _installer.InstallAsync(a.EsouiId, AddonsPath);
                if (_catalogByDir.TryGetValue(a.FolderName, out var cat)) RecordInstalled(cat, a.FolderName);
                done++;
            }
            catch (Exception ex)
            {
                Status = string.Format(Loc.Instance["Status_FailedUpdating"], a.Title, DescribeInstallFailure(a.Title, ex));
                if (_lastFailureFolderBlocked) break;   // every remaining write will fail the same way
            }
        }
        RescanInstalled();
        if (IsPro) await EnsureDependenciesForAsync(outdated.Select(a => a.FolderName).ToList());   // auto-deps = Pro
        Status = string.Format(Loc.Instance["Status_UpdatedCount"], done, outdated.Count);
    }

    private Task RemoveAsync(InstalledAddon addon)
    {
        var (ok, msg) = AddonInstaller.Uninstall(addon);
        Status = msg;
        if (ok) { RescanInstalled(); ApplyBrowse(); HasDetail = false; }
        return Task.CompletedTask;
    }

    /// <summary>Installs missing REQUIRED dependencies reachable from the given root addon folders —
    /// i.e. only those addons' own dependency tree, NOT the entire install set. Returns how many were installed.</summary>
    private async Task<int> EnsureDependenciesForAsync(IEnumerable<string> roots)
    {
        int total = 0;
        var queue = new Queue<string>(roots);
        var seen = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0)
        {
            var folder = queue.Dequeue();
            var addon = Installed.FirstOrDefault(a => a.FolderName.Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (addon is null) continue;

            foreach (var dep in addon.Dependencies)
            {
                if (Installed.Any(i => i.FolderName.Equals(dep, StringComparison.OrdinalIgnoreCase))) continue; // already installed
                if (!_catalogByDir.TryGetValue(dep, out var cat)) continue;                                    // not on ESOUI

                Status = string.Format(Loc.Instance["Status_InstallingDep"], cat.Title);
                try { await _installer.InstallAsync(cat.Id, AddonsPath); RecordInstalled(cat, dep); total++; }
                catch { continue; }
                RescanInstalled();
                if (seen.Add(dep)) queue.Enqueue(dep); // also resolve this dependency's own dependencies
            }
        }
        return total;
    }

    /// <summary>Records the ESOUI version we just installed for an addon's folders, so future update
    /// checks compare like-for-like (recorded ESOUI version vs current ESOUI version).</summary>
    private void RecordInstalled(EsouiAddon cat, string? folder = null)
    {
        foreach (var d in cat.Dirs) _state.Set(d, cat.Version);
        if (!string.IsNullOrEmpty(folder)) _state.Set(folder, cat.Version);
        _state.Save();
    }

    private void RecountUpdates() => UpdateCount = Installed.Count(a => a.UpdateAvailable);

    /// <summary>Switches the AddOns folder (from the Change-folder dialog), persists it, and rescans.</summary>
    public void SetAddonsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        AddonsPath = path;
        _settings.AddonsPathOverride = path;
        _settingsStore.Save(_settings);
        AddonsFolderFound = Directory.Exists(path);
        RescanInstalled();
        ApplyBrowse();
        Status = AddonsFolderFound
            ? string.Format(Loc.Instance["Status_AddonsFound"], Installed.Count, path)
            : Loc.Instance["Status_FolderMissing"];
    }

    // ==================== Pro: categories (assign + sortable Category column) ====================

    /// <summary>Distinct user-defined categories across installed addons (for the assign drop-down).</summary>
    public ObservableCollection<string> KnownCategories { get; } = new();

    private void RefreshKnownCategories()
    {
        var cats = Installed.Select(a => a.Category)   // effective (override else ESOUI)
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        KnownCategories.Clear();
        foreach (var c in cats) KnownCategories.Add(c);
    }

    /// <summary>Fills each installed addon's ESOUI category (the auto-default for the Category column)
    /// once category titles are known. Manual overrides still win.</summary>
    private void ApplyEsouiCategories()
    {
        foreach (var a in Installed)
            a.EsouiCategory = a.Managed && _catalogByDir.TryGetValue(a.FolderName, out var c)
                && _categoryTitlesById.TryGetValue(c.CategoryId, out var title) ? title : a.EsouiCategory;
        RefreshKnownCategories();
        InstalledView.Refresh();
    }

    /// <summary>True when the detail pane should show the category editor (Pro, installed addon selected).</summary>
    public bool ShowCategoryEditor => IsPro && DetailIsInstalled && SelectedInstalled is not null;

    private string _detailCategoryInput = "";
    /// <summary>Seeds the category combo in the detail pane (the user's override, blank if none).</summary>
    public string DetailCategoryInput { get => _detailCategoryInput; set => SetProperty(ref _detailCategoryInput, value); }

    private string _detailCategoryHint = "";
    /// <summary>Helper line under the category combo (shows the auto ESOUI category, if any).</summary>
    public string DetailCategoryHint { get => _detailCategoryHint; set => SetProperty(ref _detailCategoryHint, value); }

    /// <summary>Sets (or clears, when blank) the user's category override for an installed addon and persists it.
    /// Clearing reverts the addon to its ESOUI category.</summary>
    public void SetCategory(InstalledAddon a, string category)
    {
        if (!IsPro) return;
        category = (category ?? "").Trim();
        if (category.Length == 0) _settings.InstalledCategories.Remove(a.FolderName);
        else _settings.InstalledCategories[a.FolderName] = category;
        _settingsStore.Save(_settings);
        a.UserCategory = category;
        DetailCategoryInput = category;
        RefreshKnownCategories();
        InstalledView.Refresh();
        var now = a.Category.Length > 0 ? a.Category : Loc.Instance["Status_Uncategorized"];
        Status = category.Length == 0
            ? string.Format(Loc.Instance["Status_ClearedCategory"], a.Title, now)
            : string.Format(Loc.Instance["Status_SetCategory"], a.Title, category);
    }

    // ==================== Pro: auto-update on launch ====================

    /// <summary>Highest walkthrough version completed/skipped on this machine (0 = never).</summary>
    public int WalkthroughVersion
    {
        get => _settings.WalkthroughVersion;
        set { if (value != _settings.WalkthroughVersion) { _settings.WalkthroughVersion = value; _settingsStore.Save(_settings); } }
    }

    /// <summary>The app version last launched on this machine (for the post-update "what's new" tour).</summary>
    public string LastSeenVersion
    {
        get => _settings.LastSeenVersion;
        set { if (value != _settings.LastSeenVersion) { _settings.LastSeenVersion = value; _settingsStore.Save(_settings); } }
    }

    /// <summary>Highest Terms version accepted on this machine (gates the first-run acceptance dialog).</summary>
    public int AcceptedTermsVersion
    {
        get => _settings.AcceptedTermsVersion;
        set { if (value != _settings.AcceptedTermsVersion) { _settings.AcceptedTermsVersion = value; _settingsStore.Save(_settings); } }
    }

    /// <summary>UI language code; setting it applies live (via Loc) and persists the choice.</summary>
    public string Language
    {
        get => string.IsNullOrEmpty(_settings.Language) ? Loc.Instance.Lang : _settings.Language;
        set
        {
            Loc.Instance.Lang = value;
            if (value != _settings.Language) { _settings.Language = value; _settingsStore.Save(_settings); }
        }
    }

    /// <summary>True until the user has chosen a language at least once (drives the first-run picker).</summary>
    public bool LanguageChosen => !string.IsNullOrEmpty(_settings.Language);

    /// <summary>Pro: update all out-of-date addons automatically when the app starts.</summary>
    public bool AutoUpdateOnLaunch
    {
        get => _settings.AutoUpdateOnLaunch;
        set
        {
            if (value == _settings.AutoUpdateOnLaunch) return;
            _settings.AutoUpdateOnLaunch = value;
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>Called once after launch + license check: silently updates everything if the user opted in.</summary>
    public async Task AutoUpdateOnLaunchAsync()
    {
        if (!IsPro || !AutoUpdateOnLaunch || UpdateCount == 0) return;
        Status = Loc.Instance["Status_AutoUpdating"];
        await UpdateAllAsync();
    }

    // ==================== Pro: snapshots (backups / profiles / multi-PC sync) ====================

    public ObservableCollection<SnapshotEntry> Backups { get; } = new();
    public ObservableCollection<SnapshotEntry> Profiles { get; } = new();

    public string SyncFolder => _settings.SyncFolder;
    public bool HasSyncFolder => !string.IsNullOrWhiteSpace(_settings.SyncFolder);

    /// <summary>Reloads the backups + profiles lists from disk (call when opening Pro Tools).</summary>
    public void RefreshSnapshots()
    {
        void Fill(ObservableCollection<SnapshotEntry> target, string dir)
        {
            target.Clear();
            foreach (var e in SnapshotService.List(dir)) target.Add(e);
        }
        Fill(Backups, SnapshotService.BackupsDir);
        Fill(Profiles, SnapshotService.ProfilesDir);
    }

    /// <summary>Builds a manifest of the current install set (folder, ESOUI id, version, category).</summary>
    private SnapshotManifest BuildManifest(string name) => new()
    {
        Name = name,
        CreatedUtc = DateTime.UtcNow.ToString("o"),
        Machine = Environment.MachineName,
        AppVersion = UpdateChecker.CurrentVersion,
        Addons = Installed.Select(a => new SnapshotAddon
        {
            FolderName = a.FolderName,
            Title = a.Title,
            EsouiId = a.EsouiId,
            Version = a.DisplayVersion,
            Category = a.UserCategory,   // only the user's override travels (ESOUI category re-derives itself)
        }).ToList(),
    };

    /// <summary>Creates a timestamped local backup of the install list + SavedVariables.</summary>
    public Task BackupNowAsync()
    {
        if (!IsPro) return Task.CompletedTask;
        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            var manifest = BuildManifest($"Backup {DateTime.Now:yyyy-MM-dd HH:mm}");
            var dest = Path.Combine(SnapshotService.BackupsDir, $"backup-{stamp}.zip");
            SnapshotService.Write(manifest, SnapshotService.SavedVarsDirFor(AddonsPath), dest, SnapshotService.AddOnSettingsPathFor(AddonsPath));
            RefreshSnapshots();
            Status = string.Format(Loc.Instance["Status_BackedUp"], manifest.Addons.Count, manifest.SavedVarCount);
        }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_BackupFailed"], ex.Message); }
        return Task.CompletedTask;
    }

    /// <summary>Saves the current install set + configs as a named profile (overwrites a same-named one).</summary>
    public Task SaveProfileAsync(string name)
    {
        if (!IsPro) return Task.CompletedTask;
        name = (name ?? "").Trim();
        if (name.Length == 0) { Status = Loc.Instance["Status_EnterProfileName"]; return Task.CompletedTask; }
        try
        {
            var manifest = BuildManifest(name);
            var dest = Path.Combine(SnapshotService.ProfilesDir, SnapshotService.SafeFileName(name) + ".zip");
            SnapshotService.Write(manifest, SnapshotService.SavedVarsDirFor(AddonsPath), dest, SnapshotService.AddOnSettingsPathFor(AddonsPath));
            RefreshSnapshots();
            Status = string.Format(Loc.Instance["Status_SavedProfile"], name, manifest.Addons.Count);
        }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_SaveProfileFailed"], ex.Message); }
        return Task.CompletedTask;
    }

    /// <summary>Applies a snapshot: restores SavedVariables + categories, reinstalls any missing addons,
    /// and (optionally) removes installed addons that aren't in the snapshot.</summary>
    public async Task ApplySnapshotAsync(SnapshotEntry entry, bool restoreConfigs, bool removeExtras)
    {
        if (!IsPro || entry is null) return;
        try
        {
            IsBusy = true;

            // 1) SavedVariables (the configs)
            int restored = 0;
            if (restoreConfigs)
                restored = SnapshotService.RestoreSavedVars(entry.FilePath,
                    SnapshotService.SavedVarsDirFor(AddonsPath), SnapshotService.AddOnSettingsPathFor(AddonsPath));

            // 2) reinstall addons present in the snapshot but missing locally (ESOUI-managed only)
            var installedFolders = new HashSet<string>(Installed.Select(i => i.FolderName), StringComparer.OrdinalIgnoreCase);
            int added = 0, skipped = 0;
            foreach (var a in entry.Manifest.Addons)
            {
                if (installedFolders.Contains(a.FolderName)) continue;
                if (string.IsNullOrEmpty(a.EsouiId)) { skipped++; continue; }   // custom/private — can't auto-fetch
                Status = string.Format(Loc.Instance["Status_InstallingTitle"], a.Title);
                try { await _installer.InstallAsync(a.EsouiId, AddonsPath); added++; }
                catch { skipped++; }
            }

            // 3) optionally remove addons not in the snapshot (managed only; never touch libraries/junctions blindly)
            int removed = 0;
            if (removeExtras)
            {
                var keep = new HashSet<string>(entry.Manifest.Addons.Select(a => a.FolderName), StringComparer.OrdinalIgnoreCase);
                foreach (var inst in Installed.Where(i => i.Managed && !keep.Contains(i.FolderName)).ToList())
                {
                    var (ok, _) = AddonInstaller.Uninstall(inst);
                    if (ok) removed++;
                }
            }

            // 4) restore category assignments from the snapshot
            foreach (var a in entry.Manifest.Addons.Where(a => !string.IsNullOrWhiteSpace(a.Category)))
                _settings.InstalledCategories[a.FolderName] = a.Category;
            _settingsStore.Save(_settings);

            RescanInstalled();
            ApplyBrowse();

            var bits = new List<string>();
            if (added > 0) bits.Add(string.Format(Loc.Instance["Status_BitInstalled"], added));
            if (removed > 0) bits.Add(string.Format(Loc.Instance["Status_BitRemoved"], removed));
            if (restored > 0) bits.Add(string.Format(Loc.Instance["Status_BitRestored"], restored));
            if (skipped > 0) bits.Add(string.Format(Loc.Instance["Status_BitSkipped"], skipped));
            Status = bits.Count > 0 ? string.Format(Loc.Instance["Status_Applied"], entry.Name, string.Join(", ", bits)) : string.Format(Loc.Instance["Status_AppliedNoChange"], entry.Name);
        }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_ApplyFailed"], ex.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>Deletes a snapshot file and refreshes the lists.</summary>
    public void DeleteSnapshot(SnapshotEntry entry)
    {
        if (entry is null) return;
        try { if (File.Exists(entry.FilePath)) File.Delete(entry.FilePath); Status = string.Format(Loc.Instance["Status_Deleted"], entry.Name); }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_DeleteFailed"], ex.Message); }
        RefreshSnapshots();
    }

    /// <summary>Sets the multi-PC sync folder (typically inside a cloud-synced drive) and persists it.</summary>
    public void SetSyncFolder(string path)
    {
        _settings.SyncFolder = (path ?? "").Trim();
        _settingsStore.Save(_settings);
        OnPropertyChanged(nameof(SyncFolder));
        OnPropertyChanged(nameof(HasSyncFolder));
        Status = HasSyncFolder ? string.Format(Loc.Instance["Status_SyncFolderSet"], SyncFolder) : Loc.Instance["Status_SyncFolderCleared"];
    }

    /// <summary>Pushes the current install set + configs to the sync folder (overwrites the sync slot).</summary>
    public Task SyncPushAsync()
    {
        if (!IsPro) return Task.CompletedTask;
        if (!HasSyncFolder) { Status = Loc.Instance["Status_SetSyncFirst"]; return Task.CompletedTask; }
        try
        {
            var manifest = BuildManifest($"Sync from {Environment.MachineName}");
            var dest = Path.Combine(SyncFolder, SnapshotService.SyncFileName);
            SnapshotService.Write(manifest, SnapshotService.SavedVarsDirFor(AddonsPath), dest, SnapshotService.AddOnSettingsPathFor(AddonsPath));
            Status = string.Format(Loc.Instance["Status_Pushed"], manifest.Addons.Count, manifest.SavedVarCount);
        }
        catch (Exception ex) { Status = string.Format(Loc.Instance["Status_SyncPushFailed"], ex.Message); }
        return Task.CompletedTask;
    }

    /// <summary>Pulls the sync slot from the sync folder and applies it (restore configs, install missing).</summary>
    public async Task SyncPullAsync(bool removeExtras)
    {
        if (!IsPro) return;
        if (!HasSyncFolder) { Status = Loc.Instance["Status_SetSyncFirst"]; return; }
        var path = Path.Combine(SyncFolder, SnapshotService.SyncFileName);
        if (!File.Exists(path)) { Status = Loc.Instance["Status_NoSyncSnapshot"]; return; }
        var manifest = SnapshotService.ReadManifest(path);
        if (manifest is null) { Status = Loc.Instance["Status_SyncUnreadable"]; return; }
        await ApplySnapshotAsync(new SnapshotEntry { FilePath = path, Manifest = manifest }, restoreConfigs: true, removeExtras: removeExtras);
    }

    private static void OpenUrl(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
