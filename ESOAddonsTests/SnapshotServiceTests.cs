using System.IO;
using EsoAddons.Models;
using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class SnapshotServiceTests
{
    private static SnapshotManifest SampleManifest() => new()
    {
        Name = "PvP loadout",
        CreatedUtc = "2026-06-27T10:00:00.0000000Z",
        Machine = "SHOYRU",
        AppVersion = "0.3.18",
        Addons =
        {
            new SnapshotAddon { FolderName = "pChat", Title = "pChat", EsouiId = "123", Version = "10.0", Category = "Chat" },
            new SnapshotAddon { FolderName = "MyPrivate", Title = "Mine", EsouiId = "", Version = "1.0" },
        },
    };

    [Fact]
    public void BuildZip_then_ReadManifest_roundtrips()
    {
        using var t = new TempDir();
        var sv = t.Combine("SavedVariables");
        Directory.CreateDirectory(sv);
        File.WriteAllText(Path.Combine(sv, "pChat.lua"), "pChat_SV = {}");
        File.WriteAllText(Path.Combine(sv, "Other.lua"), "x = 1");

        var zipPath = t.Combine("snap.zip");
        SnapshotService.Write(SampleManifest(), sv, zipPath);

        var m = SnapshotService.ReadManifest(zipPath);
        Assert.NotNull(m);
        Assert.Equal("PvP loadout", m!.Name);
        Assert.Equal(2, m.Addons.Count);
        Assert.Equal("Chat", m.Addons[0].Category);
        Assert.Equal(2, m.SavedVarCount);   // stamped during build
    }

    [Fact]
    public void RestoreSavedVars_writes_lua_files_only()
    {
        using var t = new TempDir();
        var srcSv = t.Combine("src");
        Directory.CreateDirectory(srcSv);
        File.WriteAllText(Path.Combine(srcSv, "pChat.lua"), "pChat_SV = { foo = 1 }");

        var zipPath = t.Combine("snap.zip");
        SnapshotService.Write(SampleManifest(), srcSv, zipPath);

        var destSv = t.Combine("dest");
        int n = SnapshotService.RestoreSavedVars(zipPath, destSv);

        Assert.Equal(1, n);
        Assert.True(File.Exists(Path.Combine(destSv, "pChat.lua")));
        Assert.Equal("pChat_SV = { foo = 1 }", File.ReadAllText(Path.Combine(destSv, "pChat.lua")));
        // manifest.json must NOT be restored into SavedVariables
        Assert.False(File.Exists(Path.Combine(destSv, "manifest.json")));
    }

    [Fact]
    public void RestoreSavedVars_overwrites_existing()
    {
        using var t = new TempDir();
        var srcSv = t.Combine("src");
        Directory.CreateDirectory(srcSv);
        File.WriteAllText(Path.Combine(srcSv, "A.lua"), "new");
        var zipPath = t.Combine("snap.zip");
        SnapshotService.Write(SampleManifest(), srcSv, zipPath);

        var destSv = t.Combine("dest");
        Directory.CreateDirectory(destSv);
        File.WriteAllText(Path.Combine(destSv, "A.lua"), "old");

        SnapshotService.RestoreSavedVars(zipPath, destSv);
        Assert.Equal("new", File.ReadAllText(Path.Combine(destSv, "A.lua")));
    }

    [Fact]
    public void BuildZip_handles_missing_savedvars_dir()
    {
        using var t = new TempDir();
        var zipPath = t.Combine("snap.zip");
        SnapshotService.Write(SampleManifest(), t.Combine("does-not-exist"), zipPath);

        var m = SnapshotService.ReadManifest(zipPath);
        Assert.NotNull(m);
        Assert.Equal(0, m!.SavedVarCount);
    }

    [Fact]
    public void List_is_newest_first_and_skips_unreadable()
    {
        using var t = new TempDir();
        var dir = t.Combine("snaps");
        Directory.CreateDirectory(dir);

        var older = SampleManifest(); older.CreatedUtc = "2026-06-01T00:00:00.0000000Z"; older.Name = "older";
        var newer = SampleManifest(); newer.CreatedUtc = "2026-06-27T00:00:00.0000000Z"; newer.Name = "newer";
        SnapshotService.Write(older, t.Combine("none"), Path.Combine(dir, "a.zip"));
        SnapshotService.Write(newer, t.Combine("none"), Path.Combine(dir, "b.zip"));
        File.WriteAllText(Path.Combine(dir, "garbage.zip"), "not a zip");

        var list = SnapshotService.List(dir);
        Assert.Equal(2, list.Count);                 // garbage skipped
        Assert.Equal("newer", list[0].Manifest.Name); // newest first
        Assert.Equal("older", list[1].Manifest.Name);
    }

    [Fact]
    public void AddOnSettings_is_captured_and_restored()
    {
        using var t = new TempDir();
        var sv = t.Combine("SavedVariables");
        Directory.CreateDirectory(sv);
        File.WriteAllText(Path.Combine(sv, "X.lua"), "x=1");
        var settingsPath = t.Combine("AddOnSettings.txt");
        File.WriteAllText(settingsPath, "EnabledAddOns: pChat");

        var zipPath = t.Combine("snap.zip");
        SnapshotService.Write(SampleManifest(), sv, zipPath, settingsPath);

        var m = SnapshotService.ReadManifest(zipPath);
        Assert.True(m!.HasAddOnSettings);

        var destSv = t.Combine("dest");
        var destSettings = t.Combine("dest-live", "AddOnSettings.txt");
        SnapshotService.RestoreSavedVars(zipPath, destSv, destSettings);
        Assert.True(File.Exists(destSettings));
        Assert.Equal("EnabledAddOns: pChat", File.ReadAllText(destSettings));
    }

    [Fact]
    public void AddOnSettings_absent_when_not_provided()
    {
        using var t = new TempDir();
        var sv = t.Combine("sv"); Directory.CreateDirectory(sv);
        var zipPath = t.Combine("snap.zip");
        SnapshotService.Write(SampleManifest(), sv, zipPath);   // no settings path
        Assert.False(SnapshotService.ReadManifest(zipPath)!.HasAddOnSettings);
    }

    [Fact]
    public void AddOnSettingsPathFor_is_sibling_of_addons()
    {
        var addons = Path.Combine("C:", "ESO", "live", "AddOns");
        Assert.Equal(Path.Combine("C:", "ESO", "live", "AddOnSettings.txt"),
            SnapshotService.AddOnSettingsPathFor(addons));
    }

    [Fact]
    public void ReadManifest_returns_null_for_bad_zip()
    {
        using var t = new TempDir();
        var p = t.Combine("bad.zip");
        File.WriteAllText(p, "nonsense");
        Assert.Null(SnapshotService.ReadManifest(p));
    }

    [Theory]
    [InlineData("PvP", "PvP")]
    [InlineData("My/Bad:Name?", "My_Bad_Name")]
    [InlineData("   ", "profile")]
    public void SafeFileName_strips_invalid_chars(string input, string expected)
        => Assert.Equal(expected, SnapshotService.SafeFileName(input));

    [Fact]
    public void SavedVarsDirFor_is_sibling_of_addons()
    {
        var addons = Path.Combine("C:", "ESO", "live", "AddOns");
        var sv = SnapshotService.SavedVarsDirFor(addons);
        Assert.Equal(Path.Combine("C:", "ESO", "live", "SavedVariables"), sv);
    }

    [Fact]
    public void SnapshotEntry_uses_manifest_fields_for_display()
    {
        var e = new SnapshotEntry
        {
            FilePath = "x.zip",
            Manifest = new SnapshotManifest { Name = "PvE", CreatedUtc = "2026-06-27T10:00:00Z", SavedVarCount = 3,
                Addons = { new SnapshotAddon(), new SnapshotAddon() } },
        };
        Assert.Equal("PvE", e.Name);
        Assert.Contains("2 addon(s)", e.Subtitle);
        Assert.Contains("3 config(s)", e.Subtitle);
    }
}
