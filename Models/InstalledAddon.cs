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

    /// <summary>ESOUI file id once matched to the catalog (empty = unmanaged/private addon).</summary>
    private string _esouiId = "";
    public string EsouiId { get => _esouiId; set => SetProperty(ref _esouiId, value); }

    private string _latestVersion = "";
    public string LatestVersion { get => _latestVersion; set { if (SetProperty(ref _latestVersion, value)) OnPropertyChanged(nameof(UpdateAvailable)); } }

    /// <summary>The ESOUI version we recorded at install/update time (empty for addons installed outside the app).</summary>
    private string _recordedVersion = "";
    public string RecordedVersion
    {
        get => _recordedVersion;
        set { if (SetProperty(ref _recordedVersion, value)) { OnPropertyChanged(nameof(UpdateAvailable)); OnPropertyChanged(nameof(DisplayVersion)); } }
    }

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
