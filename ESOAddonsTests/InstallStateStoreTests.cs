using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class InstallStateStoreTests
{
    [Fact]
    public void Roundtrips_to_disk()
    {
        using var t = new TempDir();
        var path = t.Combine("state.json");
        var s = new InstallStateStore(path);
        s.Set("LibStub", "1.0 r7");
        s.Set("pChat", "10.0.7.4");
        s.Save();

        var reloaded = new InstallStateStore(path);
        Assert.Equal("1.0 r7", reloaded.Get("LibStub"));
        Assert.Equal("10.0.7.4", reloaded.Get("pChat"));
    }

    [Fact]
    public void Get_returns_null_when_absent()
    {
        using var t = new TempDir();
        Assert.Null(new InstallStateStore(t.Combine("x.json")).Get("Nope"));
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        using var t = new TempDir();
        var s = new InstallStateStore(t.Combine("y.json"));
        s.Set("LibStub", "1");
        Assert.Equal("1", s.Get("libstub"));
        Assert.Equal("1", s.Get("LIBSTUB"));
    }

    [Fact]
    public void Set_overwrites_previous_version()
    {
        using var t = new TempDir();
        var s = new InstallStateStore(t.Combine("z.json"));
        s.Set("A", "1.0");
        s.Set("A", "2.0");
        Assert.Equal("2.0", s.Get("A"));
    }
}
