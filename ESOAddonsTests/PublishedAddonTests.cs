using EsoAddons.Models;
using Xunit;

namespace EsoAddons.Tests;

public class PublishedAddonTests
{
    [Fact]
    public void UpdateAvailable_true_when_installed_and_newer_published()
    {
        var p = new PublishedAddon { Version = "1.1", IsInstalled = true, InstalledVersion = "1.0" };
        Assert.True(p.UpdateAvailable);
    }

    [Fact]
    public void UpdateAvailable_false_when_not_installed()
    {
        var p = new PublishedAddon { Version = "1.1", IsInstalled = false, InstalledVersion = "" };
        Assert.False(p.UpdateAvailable);
    }

    // Updating is FREE: ShowUpdate tracks UpdateAvailable with no Pro gate.
    [Fact]
    public void ShowUpdate_true_when_update_available_free_for_everyone()
    {
        var p = new PublishedAddon { Version = "2.0", IsInstalled = true, InstalledVersion = "1.9" };
        Assert.True(p.UpdateAvailable);
        Assert.True(p.ShowUpdate);
    }

    [Fact]
    public void ShowUpdate_false_when_up_to_date()
    {
        var p = new PublishedAddon { Version = "2.0", IsInstalled = true, InstalledVersion = "2.0" };
        Assert.False(p.ShowUpdate);
    }
}
