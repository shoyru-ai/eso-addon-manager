using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Roundtrips_override_path()
    {
        using var t = new TempDir();
        var path = t.Combine("settings.json");
        new SettingsStore(path).Save(new AppSettings { AddonsPathOverride = @"D:\Games\ESO\AddOns" });
        Assert.Equal(@"D:\Games\ESO\AddOns", new SettingsStore(path).Load().AddonsPathOverride);
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        using var t = new TempDir();
        Assert.Equal("", new SettingsStore(t.Combine("nope.json")).Load().AddonsPathOverride);
    }
}
