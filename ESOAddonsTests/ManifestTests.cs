using System.IO;
using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class ManifestTests
{
    [Fact]
    public void Parses_core_fields_strips_color_and_deps()
    {
        using var t = new TempDir();
        AddonFolder.Write(t.Path, "MyAddon",
            "## Title: |cFF0000My|r Addon\n## Author: Shoyru\n## Version: 1.2.3\n" +
            "## AddOnVersion: 5\n## DependsOn: LibStub LibAddonMenu-2.0>=34\n" +
            "## OptionalDependsOn: pChat\n## Description: Hello there\n");

        var a = Assert.Single(AddonScanner.Scan(t.Path));
        Assert.Equal("My Addon", a.Title);
        Assert.Equal("Shoyru", a.Author);
        Assert.Equal("1.2.3", a.Version);
        Assert.Equal("Hello there", a.Description);
        Assert.Equal(new[] { "LibStub", "LibAddonMenu-2.0" }, a.Dependencies);
        Assert.Equal(new[] { "pChat" }, a.OptionalDependencies);
    }

    [Fact]
    public void Library_detected_by_lib_prefix()
    {
        using var t = new TempDir();
        AddonFolder.Write(t.Path, "LibFoo", "## Title: LibFoo\n## Version: 1\n");
        Assert.True(Assert.Single(AddonScanner.Scan(t.Path)).IsLibrary);
    }

    [Fact]
    public void Library_detected_by_islibrary_tag_without_prefix()
    {
        using var t = new TempDir();
        AddonFolder.Write(t.Path, "CoolThing", "## Title: CoolThing\n## IsLibrary: true\n## Version: 1\n");
        Assert.True(Assert.Single(AddonScanner.Scan(t.Path)).IsLibrary);
    }

    [Fact]
    public void Non_library_addon_is_not_flagged()
    {
        using var t = new TempDir();
        AddonFolder.Write(t.Path, "CombatThing", "## Title: CombatThing\n## Version: 1\n");
        Assert.False(Assert.Single(AddonScanner.Scan(t.Path)).IsLibrary);
    }

    [Fact]
    public void Folder_without_manifest_is_skipped()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Combine("NotAnAddon"));
        Assert.Empty(AddonScanner.Scan(t.Path));
    }

    [Fact]
    public void Manifest_with_mismatched_filename_is_found_by_title_line()
    {
        using var t = new TempDir();
        var dir = t.Combine("Weird");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "something.txt"), "## Title: Weird One\n## Version: 2\n");
        Assert.Equal("Weird One", Assert.Single(AddonScanner.Scan(t.Path)).Title);
    }

    [Fact]
    public void Title_falls_back_to_folder_when_missing()
    {
        using var t = new TempDir();
        AddonFolder.Write(t.Path, "NoTitle", "## Author: x\n## Version: 1\n");
        Assert.Equal("NoTitle", Assert.Single(AddonScanner.Scan(t.Path)).Title);
    }

    [Fact]
    public void Missing_addons_path_returns_empty()
    {
        Assert.Empty(AddonScanner.Scan(@"C:\does\not\exist\anywhere\xyz"));
    }

    [Fact]
    public void Recognizes_dot_addon_manifest_extension()
    {
        // ESO manifests can be .addon (e.g. LibAddonMenu-2.0) — must be detected, not just .txt
        using var t = new TempDir();
        var dir = t.Combine("LibAddonMenu-2.0");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "LibAddonMenu-2.0.addon"),
            "## Title: LibAddonMenu-2.0\n## Version: 2.0 r43\n## IsLibrary: true\n");

        var a = Assert.Single(AddonScanner.Scan(t.Path));
        Assert.Equal("LibAddonMenu-2.0", a.FolderName);
        Assert.True(a.IsLibrary);
    }
}
