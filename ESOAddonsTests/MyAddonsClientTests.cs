using EsoAddons.Models;
using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class MyAddonsClientTests
{
    [Fact]
    public void Parses_manifest_entries()
    {
        var json = """
        { "author": "Shoyru", "addons": [
          { "name": "ShoyHouse", "title": "ShoyHouse", "version": "1.0", "description": "Teleport home", "downloadUrl": "https://x/ShoyHouse.zip" },
          { "name": "ShoyrUI",   "title": "Shoyru's UI", "version": "0.32", "description": "", "downloadUrl": "https://x/ShoyrUI.zip" }
        ]}
        """;
        var list = MyAddonsClient.ParseManifest(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("ShoyHouse", list[0].Name);
        Assert.Equal("Teleport home", list[0].Description);
        Assert.Equal("https://x/ShoyrUI.zip", list[1].DownloadUrl);
    }

    [Fact]
    public void Title_falls_back_to_name()
        => Assert.Equal("Foo", MyAddonsClient.ParseManifest("""{ "addons":[{"name":"Foo","version":"1"}] }""")[0].Title);

    [Fact]
    public void Empty_when_no_addons_array()
        => Assert.Empty(MyAddonsClient.ParseManifest("""{ "author": "x" }"""));

    [Fact]
    public void Tolerates_leading_utf8_bom()
    {
        // GitHub can serve the manifest UTF-8-with-BOM; HttpClient.GetStringAsync leaves the BOM (U+FEFF)
        // in the decoded string and JsonDocument.Parse throws on it -- which silently emptied the custom
        // addons list. Parsing must strip it. ((char)0xFEFF keeps this source file pure ASCII.)
        var json = (char)0xFEFF + """{ "addons": [ { "name": "ShoyrUI", "version": "1.94" } ] }""";
        var list = MyAddonsClient.ParseManifest(json);
        Assert.Single(list);
        Assert.Equal("ShoyrUI", list[0].Name);
    }

    [Fact]
    public void Empty_or_whitespace_returns_empty_list()
    {
        Assert.Empty(MyAddonsClient.ParseManifest(""));
        Assert.Empty(MyAddonsClient.ParseManifest("   "));
    }

    [Fact]
    public void Parses_dependencies_array()
    {
        var json = """
        { "addons": [
          { "name": "ShoyruCrosshair", "version": "1.0", "dependencies": ["LibAddonMenu-2.0"] },
          { "name": "DKCorrosiveAlert", "version": "3.01" }
        ]}
        """;
        var list = MyAddonsClient.ParseManifest(json);
        Assert.Equal(new[] { "LibAddonMenu-2.0" }, list[0].Dependencies);
        Assert.Empty(list[1].Dependencies);   // absent -> empty, not null
    }

    [Fact]
    public void Published_addon_update_logic()
    {
        var p = new PublishedAddon { Version = "1.1", IsInstalled = true, InstalledVersion = "1.0" };
        Assert.True(p.UpdateAvailable);
        Assert.False(p.ShowInstall);
        Assert.True(p.ShowRemove);

        var notInstalled = new PublishedAddon { Version = "1.0" };
        Assert.True(notInstalled.ShowInstall);
        Assert.False(notInstalled.UpdateAvailable);
    }
}
