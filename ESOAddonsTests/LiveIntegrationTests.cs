using System.IO;
using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

/// <summary>
/// Hits the real ESOUI API + CDN. Verifies the full add/update path end-to-end.
/// Network-dependent; skipped logic isn't applied since the app itself requires the API.
/// </summary>
[Trait("Category", "Integration")]
public class LiveIntegrationTests
{
    [Fact]
    public async Task Catalog_loads_and_has_known_library()
    {
        var client = new EsouiClient();
        var catalog = await client.GetCatalogAsync();
        Assert.True(catalog.Count > 1000, $"expected a large catalog, got {catalog.Count}");
        Assert.Contains(catalog, a => a.Dirs.Contains("LibStub"));
    }

    [Fact]
    public async Task Install_LibStub_end_to_end_then_uninstall()
    {
        using var t = new TempDir();
        var client = new EsouiClient();
        var installer = new AddonInstaller(client);

        // ADD (real download + extract)
        await installer.InstallAsync("44", t.Path); // 44 = LibStub
        var libDir = t.Combine("LibStub");
        Assert.True(Directory.Exists(libDir), "LibStub folder should exist after install");
        Assert.True(Directory.GetFiles(libDir).Length > 0, "LibStub should contain files");

        // scanner should now see it as an installed library
        var scanned = AddonScanner.Scan(t.Path);
        Assert.Contains(scanned, a => a.FolderName == "LibStub" && a.IsLibrary);

        // REMOVE
        var addon = scanned.First(a => a.FolderName == "LibStub");
        var (ok, _) = AddonInstaller.Uninstall(addon);
        Assert.True(ok);
        Assert.False(Directory.Exists(libDir));
    }
}
