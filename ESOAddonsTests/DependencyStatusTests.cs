using EsoAddons.Models;
using Xunit;

namespace EsoAddons.Tests;

public class DependencyStatusTests
{
    [Fact]
    public void Installed_shows_green_check_and_no_get()
    {
        var d = new DependencyStatus { Name = "LibStub", IsInstalled = true };
        Assert.Equal("✓", d.Glyph);
        Assert.Equal("installed", d.StateText);
        Assert.False(d.ShowGet);
    }

    [Fact]
    public void Missing_required_and_gettable_shows_cross_and_get()
    {
        var d = new DependencyStatus { Name = "LibAddonMenu-2.0", IsInstalled = false, IsOptional = false, IsGettable = true };
        Assert.Equal("✗", d.Glyph);
        Assert.True(d.IsMissingRequired);
        Assert.True(d.ShowGet);
        Assert.Equal("missing", d.StateText);
    }

    [Fact]
    public void Missing_required_not_on_esoui_is_soft_not_red()
    {
        // A required dep that's not on ESOUI is almost always bundled inside the addon — treat it as a
        // muted note, NOT an alarming red ✗.
        var d = new DependencyStatus { Name = "PrivateLib", IsInstalled = false, IsGettable = false };
        Assert.Equal("–", d.Glyph);
        Assert.False(d.ShowGet);
        Assert.False(d.IsMissingRequired);   // not flagged as an actionable problem
        Assert.True(d.IsMissingSoft);
        Assert.Contains("likely bundled", d.StateText);
    }

    [Fact]
    public void Optional_missing_is_neutral_not_red()
    {
        var d = new DependencyStatus { Name = "ScootworksCombat", IsInstalled = false, IsOptional = true, IsGettable = false };
        Assert.Equal("–", d.Glyph);
        Assert.True(d.IsMissingSoft);
        Assert.False(d.IsMissingRequired);   // must NOT render as an alarming required-missing
        Assert.False(d.ShowGet);
        Assert.Contains("optional", d.StateText);
    }

    [Fact]
    public void Optional_installed_shows_check()
        => Assert.Equal("✓", new DependencyStatus { Name = "pChat", IsOptional = true, IsInstalled = true }.Glyph);
}
