using EsoAddons.Models;
using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class SettingsAndCategoryTests
{
    [Fact]
    public void Settings_roundtrip_new_pro_fields()
    {
        using var t = new TempDir();
        var path = t.Combine("settings.json");
        var store = new SettingsStore(path);

        var s = store.Load();
        s.AutoUpdateOnLaunch = true;
        s.SyncFolder = @"C:\Users\jaker\OneDrive\eso-sync";
        s.InstalledCategories["pChat"] = "Chat";
        store.Save(s);

        var reloaded = new SettingsStore(path).Load();
        Assert.True(reloaded.AutoUpdateOnLaunch);
        Assert.Equal(@"C:\Users\jaker\OneDrive\eso-sync", reloaded.SyncFolder);
        Assert.Equal("Chat", reloaded.InstalledCategories["pChat"]);
    }

    [Fact]
    public void InstalledCategories_lookup_is_case_insensitive_after_reload()
    {
        using var t = new TempDir();
        var path = t.Combine("settings.json");
        var store = new SettingsStore(path);
        var s = store.Load();
        s.InstalledCategories["pChat"] = "Chat";
        store.Save(s);

        // The case-insensitive comparer must survive the JSON round-trip (System.Text.Json drops it).
        var reloaded = new SettingsStore(path).Load();
        Assert.True(reloaded.InstalledCategories.TryGetValue("PCHAT", out var v));
        Assert.Equal("Chat", v);
    }

    [Fact]
    public void Defaults_are_off_and_empty()
    {
        var s = new AppSettings();
        Assert.False(s.AutoUpdateOnLaunch);
        Assert.Equal("", s.SyncFolder);
        Assert.Empty(s.InstalledCategories);
        Assert.Equal(0, s.WalkthroughVersion);   // first run (0) shows the walkthrough
    }

    [Fact]
    public void WalkthroughVersion_round_trips()
    {
        using var t = new TempDir();
        var path = t.Combine("settings.json");
        var s = new SettingsStore(path).Load();
        s.WalkthroughVersion = 2;
        new SettingsStore(path).Save(s);
        Assert.Equal(2, new SettingsStore(path).Load().WalkthroughVersion);
    }

    [Fact]
    public void AcceptedTermsVersion_round_trips_and_defaults_zero()
    {
        Assert.Equal(0, new AppSettings().AcceptedTermsVersion);
        using var t = new TempDir();
        var path = t.Combine("settings.json");
        var s = new SettingsStore(path).Load();
        s.AcceptedTermsVersion = 1;
        new SettingsStore(path).Save(s);
        Assert.Equal(1, new SettingsStore(path).Load().AcceptedTermsVersion);
    }

    [Fact]
    public void LastSeenVersion_round_trips_and_defaults_empty()
    {
        Assert.Equal("", new AppSettings().LastSeenVersion);
        using var t = new TempDir();
        var path = t.Combine("settings.json");
        var s = new SettingsStore(path).Load();
        s.LastSeenVersion = "0.3.22";
        new SettingsStore(path).Save(s);
        Assert.Equal("0.3.22", new SettingsStore(path).Load().LastSeenVersion);
    }

    [Fact]
    public void Effective_category_prefers_user_override_then_esoui()
    {
        var a = new InstalledAddon { FolderName = "X" };
        Assert.Equal("", a.Category);                 // nothing set

        a.EsouiCategory = "Combat";
        Assert.Equal("Combat", a.Category);           // falls back to ESOUI

        a.UserCategory = "PvP";
        Assert.Equal("PvP", a.Category);              // override wins

        a.UserCategory = "";
        Assert.Equal("Combat", a.Category);           // clearing reverts to ESOUI
    }
}
