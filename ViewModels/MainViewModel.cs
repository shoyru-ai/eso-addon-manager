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
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private IReadOnlyList<EsouiAddon> _catalog = Array.Empty<EsouiAddon>();
    private Dictionary<string, EsouiAddon> _catalogByDir = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel()
    {
        _installer = new AddonInstaller(_client);
        _settings = _settingsStore.Load();
        var (path, found) = AddonsLocator.Resolve(_settings.AddonsPathOverride);
        AddonsPath = path;
        AddonsFolderFound = found;

        InstalledView = CollectionViewSource.GetDefaultView(Installed);
        InstalledView.Filter = o => PassesFilter(((InstalledAddon)o).IsLibrary);
        BrowseView = CollectionViewSource.GetDefaultView(SearchResults);
        BrowseView.Filter = o => PassesFilter(((EsouiAddon)o).IsLibrary);

        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(true));
        SearchCommand = new RelayCommand(_ => ApplyBrowse());
        SetSortCommand = new RelayCommand(s => Sort = Enum.Parse<BrowseSort>((string)s!));
        InstallCommand = new AsyncRelayCommand(a => InstallAsync((EsouiAddon)a!), a => a is EsouiAddon);
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

    // ---- app self-update ----
    private bool _appUpdateAvailable;
    public bool AppUpdateAvailable { get => _appUpdateAvailable; set => SetProperty(ref _appUpdateAvailable, value); }
    private string _appUpdateVersion = "";
    public string AppUpdateVersion { get => _appUpdateVersion; set => SetProperty(ref _appUpdateVersion, value); }
    public string AppUpdateNotes { get; private set; } = "";
    public string AppUpdateExeUrl { get; private set; } = "";
    public string AppUpdateReleaseUrl { get; private set; } = "";

    /// <summary>Checks GitHub Releases; if a newer app version exists, surfaces the update banner.</summary>
    public async Task CheckForAppUpdateAsync()
    {
        var info = await new UpdateChecker().CheckAsync();
        if (info is { IsNewer: true })
        {
            AppUpdateExeUrl = info.ExeUrl;
            AppUpdateReleaseUrl = info.ReleaseUrl;
            AppUpdateNotes = info.Notes;
            AppUpdateVersion = info.Version;
            AppUpdateAvailable = true;
            Status = $"A new version (v{info.Version}) is available.";
        }
    }

    public ObservableCollection<InstalledAddon> Installed { get; } = new();
    public ObservableCollection<EsouiAddon> SearchResults { get; } = new();
    public ICollectionView InstalledView { get; }
    public ICollectionView BrowseView { get; }

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand InstallCommand { get; }
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
    public bool HasDetail { get => _hasDetail; set => SetProperty(ref _hasDetail, value); }
    private bool _detailIsInstalled;
    public bool DetailIsInstalled { get => _detailIsInstalled; set => SetProperty(ref _detailIsInstalled, value); }
    private string _detailTitle = "";
    public string DetailTitle { get => _detailTitle; set => SetProperty(ref _detailTitle, value); }
    private string _detailMeta = "";
    public string DetailMeta { get => _detailMeta; set => SetProperty(ref _detailMeta, value); }
    private string _detailImageUrl = "";
    public string DetailImageUrl { get => _detailImageUrl; set => SetProperty(ref _detailImageUrl, value); }
    private string _detailDescription = "";
    public string DetailDescription { get => _detailDescription; set => SetProperty(ref _detailDescription, value); }
    private string _detailChangeLog = "";
    public string DetailChangeLog { get => _detailChangeLog; set { if (SetProperty(ref _detailChangeLog, value)) OnPropertyChanged(nameof(HasChangeLog)); } }
    public bool HasChangeLog => !string.IsNullOrWhiteSpace(DetailChangeLog);
    private string _detailPageUrl = "";
    public string DetailPageUrl { get => _detailPageUrl; set => SetProperty(ref _detailPageUrl, value); }
    public ObservableCollection<DependencyStatus> DetailDependencies { get; } = new();
    private bool _hasDependencies;
    public bool HasDependencies { get => _hasDependencies; set => SetProperty(ref _hasDependencies, value); }
    private bool _showInstall;
    public bool ShowInstall { get => _showInstall; set => SetProperty(ref _showInstall, value); }
    private bool _showManage;
    public bool ShowManage { get => _showManage; set => SetProperty(ref _showManage, value); }
    private bool _detailUpdateAvailable;
    public bool DetailUpdateAvailable { get => _detailUpdateAvailable; set => SetProperty(ref _detailUpdateAvailable, value); }

    // detail action targets
    private EsouiAddon? _detailInstallTarget;
    public EsouiAddon? DetailInstallTarget { get => _detailInstallTarget; set => SetProperty(ref _detailInstallTarget, value); }
    private InstalledAddon? _detailRemoveTarget;
    public InstalledAddon? DetailRemoveTarget { get => _detailRemoveTarget; set => SetProperty(ref _detailRemoveTarget, value); }

    public string SearchText { get; set; } = "";

    private string _status = "Ready.";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    private int _updateCount;
    public int UpdateCount { get => _updateCount; set { if (SetProperty(ref _updateCount, value)) OnPropertyChanged(nameof(UpdateSummary)); } }
    public string UpdateSummary => UpdateCount == 0 ? "All up to date" : $"{UpdateCount} update(s) available";

    // ---- load ----
    public async Task LoadAsync(bool refreshCatalog = false)
    {
        try
        {
            IsBusy = true;
            Status = "Fetching ESOUI catalog…";
            _catalog = await _client.GetCatalogAsync(refreshCatalog);
            _catalogByDir = new Dictionary<string, EsouiAddon>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _catalog)
                foreach (var dir in c.Dirs)
                    if (!_catalogByDir.TryGetValue(dir, out var existing) || c.Downloads > existing.Downloads)
                        _catalogByDir[dir] = c;

            RescanInstalled();

            // categories (buckets) — derive from catalog so only non-empty ones show
            try
            {
                var cats = await _client.GetCategoriesAsync();
                var titleById = cats.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Title);
                var counts = _catalog.GroupBy(a => a.CategoryId).ToDictionary(g => g.Key, g => g.Count());
                Categories.Clear();
                Categories.Add(new Category { Id = "", Title = "All Categories" });
                foreach (var kv in counts.Where(k => titleById.ContainsKey(k.Key)).OrderBy(k => titleById[k.Key]))
                    Categories.Add(new Category { Id = kv.Key, Title = titleById[kv.Key], Count = kv.Value });
                _selectedCategory = Categories[0];
                OnPropertyChanged(nameof(SelectedCategory));
            }
            catch { /* categories are optional; browse still works */ }

            ApplyBrowse(); // populate Browse by default (top downloads) so it's never blank
            Status = $"{Installed.Count} addons installed · catalog: {_catalog.Count:N0} addons.";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
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
                a.ThumbUrl = match.ThumbUrl;
                if (match.IsLibrary) a.IsLibrary = true;
                var recorded = _state.Get(a.FolderName);
                if (recorded is not null) a.RecordedVersion = recorded;
            }
            Installed.Add(a);
        }
        RecountUpdates();
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
            SearchResults.Add(a);
        }
        BrowseView.Refresh();
        var where = SelectedCategory is { Id.Length: > 0 } c ? $" · {c.Title}" : "";
        Status = $"Showing {SearchResults.Count} addon(s){where}.";
    }

    // ---- detail population ----
    private async Task ShowInstalledDetail(InstalledAddon a)
    {
        HasDetail = true;
        DetailIsInstalled = true;
        ShowInstall = false;
        ShowManage = true;
        DetailUpdateAvailable = a.UpdateAvailable;
        DetailInstallTarget = null;
        DetailRemoveTarget = a;
        DetailTitle = a.Title;
        DetailMeta = a.Managed
            ? $"Installed v{a.Version}  ·  latest v{a.LatestVersion}  ·  by {a.Author}"
            : $"Installed v{a.Version}  ·  by {a.Author}  ·  (not on ESOUI)";
        DetailImageUrl = a.ThumbUrl;
        DetailDescription = a.Description;
        DetailChangeLog = "";
        DetailPageUrl = "";
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
        ShowInstall = true;
        ShowManage = false;
        DetailUpdateAvailable = false;
        DetailRemoveTarget = null;
        DetailInstallTarget = a;
        DetailTitle = a.Title;
        DetailMeta = $"v{a.Version}  ·  {a.DownloadsDisplay} downloads  ·  updated {a.LastUpdated}  ·  by {a.Author}";
        DetailImageUrl = a.ThumbUrl;
        DetailPageUrl = a.FileInfoUri;
        DetailDescription = "Loading description…";
        DetailChangeLog = "";
        DetailDependencies.Clear();
        HasDependencies = false;

        try
        {
            var d = await _client.GetDetailsAsync(a.Id);
            if (ReferenceEquals(_selectedBrowse, a))
            {
                if (!string.IsNullOrWhiteSpace(d.ImageUrl)) DetailImageUrl = d.ImageUrl;
                DetailDescription = string.IsNullOrWhiteSpace(d.Description) ? "(no description)" : d.Description;
                DetailChangeLog = d.ChangeLog;
            }
        }
        catch (Exception ex) { DetailDescription = "Could not load description: " + ex.Message; }
    }

    private void BuildDependencyList(InstalledAddon a)
    {
        DetailDependencies.Clear();
        var installed = new HashSet<string>(Installed.Select(i => i.FolderName), StringComparer.OrdinalIgnoreCase);
        foreach (var d in a.Dependencies)
            DetailDependencies.Add(new DependencyStatus { Name = d, IsInstalled = installed.Contains(d), IsOptional = false });
        foreach (var d in a.OptionalDependencies)
            DetailDependencies.Add(new DependencyStatus { Name = d, IsInstalled = installed.Contains(d), IsOptional = true });
        HasDependencies = DetailDependencies.Count > 0;
    }

    // ---- actions ----
    private async Task InstallAsync(EsouiAddon addon)
    {
        try
        {
            addon.Status = "Installing…";
            Status = $"Installing {addon.Title}…";
            await _installer.InstallAsync(addon.Id, AddonsPath);
            RecordInstalled(addon);
            RescanInstalled();
            var deps = await EnsureDependenciesForAsync(addon.Dirs);
            addon.IsInstalled = true;
            addon.Status = "Installed";
            ApplyBrowse();
            Status = deps > 0 ? $"Installed {addon.Title} (+{deps} dependency/ies)." : $"Installed {addon.Title}.";
        }
        catch (Exception ex) { addon.Status = "Failed"; Status = $"Install failed: {ex.Message}"; }
    }

    private async Task InstallByDirAsync(string dir)
    {
        if (!_catalogByDir.TryGetValue(dir, out var cat)) { Status = $"{dir} not found on ESOUI."; return; }
        Status = $"Installing {cat.Title}…";
        try
        {
            await _installer.InstallAsync(cat.Id, AddonsPath);
            RecordInstalled(cat, dir);
            RescanInstalled(); // restores selection + refreshes the dependency ✓/✗ list
            Status = $"Installed {cat.Title}.";
        }
        catch (Exception ex) { Status = $"Install failed: {ex.Message}"; }
    }

    private async Task UpdateAsync(InstalledAddon addon)
    {
        try
        {
            addon.Status = "Updating…";
            Status = $"Updating {addon.Title}…";
            await _installer.InstallAsync(addon.EsouiId, AddonsPath);
            if (_catalogByDir.TryGetValue(addon.FolderName, out var cat)) RecordInstalled(cat, addon.FolderName);
            RescanInstalled();
            await EnsureDependenciesForAsync(new[] { addon.FolderName });
            Status = $"Updated {addon.Title}.";
        }
        catch (Exception ex) { addon.Status = "Failed"; Status = $"Update failed: {ex.Message}"; }
    }

    private async Task UpdateAllAsync()
    {
        var outdated = Installed.Where(a => a.UpdateAvailable).ToList();
        int done = 0;
        foreach (var a in outdated)
        {
            try
            {
                Status = $"Updating {a.Title}… ({done + 1}/{outdated.Count})";
                await _installer.InstallAsync(a.EsouiId, AddonsPath);
                if (_catalogByDir.TryGetValue(a.FolderName, out var cat)) RecordInstalled(cat, a.FolderName);
                done++;
            }
            catch (Exception ex) { Status = $"Failed updating {a.Title}: {ex.Message}"; }
        }
        RescanInstalled();
        await EnsureDependenciesForAsync(outdated.Select(a => a.FolderName).ToList());
        Status = $"Updated {done} of {outdated.Count} addon(s).";
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

                Status = $"Installing dependency {cat.Title}…";
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
            ? $"{Installed.Count} addons found in {path}."
            : "That folder doesn't exist — pick your '…\\Elder Scrolls Online\\live\\AddOns' folder.";
    }

    private static void OpenUrl(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
