using Velopack;
using Velopack.Sources;

namespace EsoAddons.Services;

/// <summary>Wraps Velopack's UpdateManager for the app's self-update (GitHub Releases, channel-aware).
/// PPE uses the "ppe" channel + GitHub pre-releases; PROD uses the default channel + stable releases.</summary>
public class AppUpdateService
{
    private const string RepoUrl = "https://github.com/shoyru-ai/eso-addon-manager";

    private readonly UpdateManager _mgr;
    private readonly bool _ppe;
    private Velopack.UpdateInfo? _pending;   // qualified: EsoAddons.Services also has an UpdateInfo record

    public AppUpdateService(bool ppe)
    {
        _ppe = ppe;
        var source = new GithubSource(RepoUrl, null, prerelease: ppe);
        _mgr = new UpdateManager(source, new UpdateOptions { ExplicitChannel = ppe ? "ppe" : null });
    }

    /// <summary>True only when running from an installed (Velopack) copy — false in dev / portable builds,
    /// where self-update is a no-op.</summary>
    public bool IsInstalled => _mgr.IsInstalled;

    public record UpdateAvailable(string Version, string Notes);

    /// <summary>Checks for a newer release. Returns null when not installed or already up to date.</summary>
    public async Task<UpdateAvailable?> CheckAsync()
    {
        if (!_mgr.IsInstalled) return null;
        _pending = await _mgr.CheckForUpdatesAsync();
        if (_pending is null) return null;
        var a = _pending.TargetFullRelease;
        return new UpdateAvailable(a.Version.ToString(), a.NotesMarkdown ?? "");
    }

    /// <summary>Downloads the pending update (delta when possible), reporting 0-100 progress.</summary>
    public Task DownloadAsync(Action<int> progress)
        => _pending is null ? Task.CompletedTask : _mgr.DownloadUpdatesAsync(_pending, progress);

    /// <summary>Applies the downloaded update and restarts, re-passing --ppe so the channel persists.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is null) return;
        var restartArgs = _ppe ? new[] { "--ppe" } : Array.Empty<string>();
        _mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease, restartArgs);
    }
}
