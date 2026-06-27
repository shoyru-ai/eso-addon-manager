using EsoAddons.Mvvm;
using EsoAddons.Services;

namespace EsoAddons.Models;

/// <summary>An addon published by Shoyru (from the GitHub manifest), shown in the "Shoyru's Addons" tab.</summary>
public class PublishedAddon : ObservableObject
{
    public string Name { get; init; } = "";        // AddOns folder name
    public string Title { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    /// <summary>Required library folder names (from the addon's ## DependsOn), shown in the detail pane.</summary>
    public List<string> Dependencies { get; init; } = new();

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set { if (SetProperty(ref _isInstalled, value)) RaiseDerived(); }
    }

    private string _installedVersion = "";
    public string InstalledVersion
    {
        get => _installedVersion;
        set { if (SetProperty(ref _installedVersion, value)) RaiseDerived(); }
    }

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public bool UpdateAvailable =>
        IsInstalled && !string.IsNullOrWhiteSpace(InstalledVersion) && VersionCompare.IsNewer(Version, InstalledVersion);
    public bool ShowInstall => !IsInstalled;
    public bool ShowRemove => IsInstalled;

    // Addon updates are a Pro feature. The VM sets ProUpdates = IsPro.
    private bool _proUpdates;
    public bool ProUpdates { get => _proUpdates; set { if (SetProperty(ref _proUpdates, value)) OnPropertyChanged(nameof(ShowUpdate)); } }
    public bool ShowUpdate => ProUpdates && UpdateAvailable;

    public string StatusLabel =>
        !IsInstalled ? $"v{Version}"
        : UpdateAvailable ? $"installed v{InstalledVersion} · v{Version} available"
        : $"installed v{InstalledVersion}";

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(ShowUpdate));
        OnPropertyChanged(nameof(ShowInstall));
        OnPropertyChanged(nameof(ShowRemove));
        OnPropertyChanged(nameof(StatusLabel));
    }
}
