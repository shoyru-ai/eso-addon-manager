using EsoAddons.Models;
using Xunit;

namespace EsoAddons.Tests;

public class InstalledAddonTests
{
    [Fact]
    public void UpdateAvailable_true_when_managed_and_versions_differ()
    {
        var a = new InstalledAddon { Version = "1.0", EsouiId = "7", LatestVersion = "1.1" };
        Assert.True(a.UpdateAvailable);
    }

    [Fact]
    public void Missing_dependency_flags_health_and_tooltip()
    {
        var healthy = new InstalledAddon { MissingDeps = "" };
        Assert.False(healthy.HasMissingDeps);

        var broken = new InstalledAddon { MissingDeps = "LibAddonMenu-2.0" };
        Assert.True(broken.HasMissingDeps);
        Assert.Contains("LibAddonMenu-2.0", broken.HealthTooltip);
    }

    [Fact]
    public void UpdateAvailable_false_when_versions_match()
    {
        var a = new InstalledAddon { Version = "1.0", EsouiId = "7", LatestVersion = "1.0" };
        Assert.False(a.UpdateAvailable);
    }

    [Fact]
    public void UpdateAvailable_false_when_unmanaged_even_with_latest()
    {
        var a = new InstalledAddon { Version = "1.0", LatestVersion = "9.9" }; // no EsouiId
        Assert.False(a.Managed);
        Assert.False(a.UpdateAvailable);
    }

    [Fact]
    public void UpdateAvailable_ignores_case_and_surrounding_whitespace()
    {
        var a = new InstalledAddon { Version = " 1.0 ", EsouiId = "7", LatestVersion = "1.0" };
        Assert.False(a.UpdateAvailable);
    }

    [Fact]
    public void Managed_true_only_with_esoui_id()
    {
        Assert.True(new InstalledAddon { EsouiId = "7" }.Managed);
        Assert.False(new InstalledAddon { EsouiId = "" }.Managed);
    }

    [Fact]
    public void UpdateAvailable_prefers_recorded_version_over_manifest()
    {
        // manifest says 3.0, but we recorded the ESOUI version 3.0.42 at install; latest is 3.0.42 -> no update
        var a = new InstalledAddon { Version = "3.0", EsouiId = "7", LatestVersion = "3.0.42", RecordedVersion = "3.0.42" };
        Assert.False(a.UpdateAvailable);
    }

    [Fact]
    public void UpdateAvailable_true_when_recorded_older_than_latest()
    {
        var a = new InstalledAddon { Version = "whatever", EsouiId = "7", LatestVersion = "1.3.7", RecordedVersion = "1.3.6" };
        Assert.True(a.UpdateAvailable);
    }

    [Fact]
    public void UpdateAvailable_falls_back_to_manifest_semantic_when_no_record()
    {
        var a = new InstalledAddon { Version = "1.3.6", EsouiId = "7", LatestVersion = "1.3.7" };
        Assert.True(a.UpdateAvailable);
    }

    [Fact]
    public void UpdateAvailable_false_when_manifest_newer_than_catalog()
    {
        // DKCorrosiveAlert real case: manifest 3.01 ahead of ESOUI 1.04 -> not an update
        var a = new InstalledAddon { Version = "3.01", EsouiId = "7", LatestVersion = "1.04" };
        Assert.False(a.UpdateAvailable);
    }

    [Fact]
    public void DisplayVersion_prefers_recorded_then_manifest()
    {
        Assert.Equal("3.0.42", new InstalledAddon { Version = "3.0", EsouiId = "7", RecordedVersion = "3.0.42" }.DisplayVersion);
        Assert.Equal("3.0", new InstalledAddon { Version = "3.0", EsouiId = "7" }.DisplayVersion);
    }

    // Updating add-ons is FREE for everyone: the Update button shows whenever an update is available,
    // with no Pro gate (auto-update-on-launch remains the Pro feature, gated in the VM).
    [Fact]
    public void ShowUpdate_true_when_update_available_free_for_everyone()
    {
        var a = new InstalledAddon { Version = "1.0", EsouiId = "7", LatestVersion = "1.1" };
        Assert.True(a.UpdateAvailable);
        Assert.True(a.ShowUpdate);
    }

    [Fact]
    public void ShowUpdate_false_when_up_to_date()
    {
        var a = new InstalledAddon { Version = "1.1", EsouiId = "7", LatestVersion = "1.1" };
        Assert.False(a.ShowUpdate);
    }

    [Fact]
    public void ShowUpdate_false_when_unmanaged()
    {
        var a = new InstalledAddon { Version = "1.0", LatestVersion = "9.9" }; // no EsouiId
        Assert.False(a.ShowUpdate);
    }
}
