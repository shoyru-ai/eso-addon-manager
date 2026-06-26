using EsoAddons.Mvvm;

namespace EsoAddons.Models;

/// <summary>A catalog entry from the ESOUI (mmoui) addon list.</summary>
public class EsouiAddon : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public long Downloads { get; init; }
    public string FileInfoUri { get; init; } = "";
    public long LastUpdateMs { get; init; }
    public string CategoryId { get; init; } = "";
    /// <summary>Folder names this addon installs (from UIDir) — used to match installed addons.</summary>
    public List<string> Dirs { get; init; } = new();
    public string ThumbUrl { get; init; } = "";
    /// <summary>True if this is in the ESOUI "Libraries" category (id 53).</summary>
    public bool IsLibrary { get; init; }

    public string DownloadsDisplay => Downloads.ToString("N0");
    public string LastUpdated => LastUpdateMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(LastUpdateMs).LocalDateTime.ToString("yyyy-MM-dd")
        : "";

    private string _status = "";
    /// <summary>Transient UI status, e.g. "Installing…", "Installed".</summary>
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private bool _isInstalled;
    public bool IsInstalled { get => _isInstalled; set => SetProperty(ref _isInstalled, value); }
}
