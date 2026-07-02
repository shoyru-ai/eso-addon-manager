using EsoAddons.Mvvm;
using EsoAddons.Services;

namespace EsoAddons.Models;

/// <summary>An addon found in the live AddOns folder (parsed from its .txt manifest).</summary>
public class InstalledAddon : ObservableObject
{
    public string FolderName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public string Path { get; init; } = "";
    public List<string> Dependencies { get; init; } = new();
    public List<string> OptionalDependencies { get; init; } = new();

    private bool _isLibrary;
    public bool IsLibrary { get => _isLibrary; set => SetProperty(ref _isLibrary, value); }

    private string _thumbUrl = "";
    public string ThumbUrl { get => _thumbUrl; set => SetProperty(ref _thumbUrl, value); }

    /// <summary>Full-size first screenshot (from the catalog) for the detail pane + enlarge viewer.</summary>
    private string _imageUrl = "";
    public string ImageUrl { get => _imageUrl; set => SetProperty(ref _imageUrl, value); }

    /// <summary>All full-size screenshots (from the catalog) — the enlarge viewer cycles through these.</summary>
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>Pro: the user's manual category override (empty = fall back to the ESOUI category).</summary>
    private string _userCategory = "";
    public string UserCategory { get => _userCategory; set { if (SetProperty(ref _userCategory, value)) OnPropertyChanged(nameof(Category)); } }

    /// <summary>The addon's category on ESOUI (auto-filled from the catalog; empty for unmanaged addons).</summary>
    private string _esouiCategory = "";
    public string EsouiCategory { get => _esouiCategory; set { if (SetProperty(ref _esouiCategory, value)) OnPropertyChanged(nameof(Category)); } }

    /// <summary>Effective category shown in the sortable Installed column: the user's override if set,
    /// otherwise the ESOUI category.</summary>
    public string Category => !string.IsNullOrWhiteSpace(_userCategory) ? _userCategory : _esouiCategory;

    /// <summary>ESOUI file id once matched to the catalog (empty = unmanaged/private addon).</summary>
    private string _esouiId = "";
    public string EsouiId { get => _esouiId; set => SetProperty(ref _esouiId, value); }

    private string _latestVersion = "";
    public string LatestVersion { get => _latestVersion; set { if (SetProperty(ref _latestVersion, value)) { OnPropertyChanged(nameof(UpdateAvailable)); OnPropertyChanged(nameof(ShowUpdate)); } } }

    /// <summary>When this addon was last updated on ESOUI (Unix ms; 0 for unmanaged/private addons).</summary>
    private long _lastUpdateMs;
    public long LastUpdateMs { get => _lastUpdateMs; set { if (SetProperty(ref _lastUpdateMs, value)) OnPropertyChanged(nameof(LastUpdated)); } }
    /// <summary>Last-updated date for display (empty when unknown).</summary>
    public string LastUpdated => _lastUpdateMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(_lastUpdateMs).LocalDateTime.ToString("yyyy-MM-dd")
        : "";

    /// <summary>Show the Update button whenever an update is available (updating is free).</summary>
    public bool ShowUpdate => UpdateAvailable;

    /// <summary>The ESOUI version we recorded at install/update time (empty for addons installed outside the app).</summary>
    private string _recordedVersion = "";
    public string RecordedVersion
    {
        get => _recordedVersion;
        set { if (SetProperty(ref _recordedVersion, value)) { OnPropertyChanged(nameof(UpdateAvailable)); OnPropertyChanged(nameof(ShowUpdate)); OnPropertyChanged(nameof(DisplayVersion)); } }
    }

    // Health: required dependencies that aren't installed (set by the VM from the installed set).
    private string _missingDeps = "";
    public string MissingDeps
    {
        get => _missingDeps;
        set { if (SetProperty(ref _missingDeps, value)) { OnPropertyChanged(nameof(HasMissingDeps)); OnPropertyChanged(nameof(HealthTooltip)); } }
    }
    /// <summary>True when this addon is missing a required dependency and likely won't work in-game.</summary>
    public bool HasMissingDeps => _missingDeps.Length > 0;
    public string HealthTooltip => HasMissingDeps
        ? $"May not work — missing required dependency: {_missingDeps}"
        : "All dependencies present";

    public bool Managed => !string.IsNullOrEmpty(EsouiId);

    /// <summary>Version shown in the list: the recorded ESOUI version if we have one, else the manifest version.</summary>
    public string DisplayVersion => Managed && RecordedVersion.Length > 0 ? RecordedVersion : Version;

    /// <summary>Baseline used for update comparison: recorded ESOUI version, else the manifest version.</summary>
    private string Baseline => RecordedVersion.Length > 0 ? RecordedVersion : Version;

    /// <summary>True when the catalog's version is numerically newer than what's installed.</summary>
    public bool UpdateAvailable =>
        Managed && !string.IsNullOrWhiteSpace(LatestVersion) && VersionCompare.IsNewer(LatestVersion, Baseline);

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
}
