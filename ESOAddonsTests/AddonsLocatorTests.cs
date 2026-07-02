using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class AddonsLocatorTests
{
    [Fact]
    public void Pick_returns_first_existing_candidate()
    {
        var (path, found) = AddonsLocator.Pick(new[] { "A", "B", "C" }, c => c == "B");
        Assert.True(found);
        Assert.Equal("B", path);
    }

    [Fact]
    public void Pick_returns_first_and_notfound_when_none_exist()
    {
        var (path, found) = AddonsLocator.Pick(new[] { "A", "B" }, _ => false);
        Assert.False(found);
        Assert.Equal("A", path);
    }

    [Fact]
    public void Pick_handles_empty_list()
    {
        var (path, found) = AddonsLocator.Pick(Array.Empty<string>(), _ => true);
        Assert.False(found);
        Assert.Equal("", path);
    }

    [Fact]
    public void Resolve_prefers_an_existing_override()
    {
        using var t = new TempDir();          // a real, existing directory
        var (path, found) = AddonsLocator.Resolve(t.Path);
        Assert.True(found);
        Assert.Equal(t.Path, path);
    }

    [Fact]
    public void DefaultCandidates_all_target_the_addons_subpath()
    {
        var candidates = AddonsLocator.DefaultCandidates();
        Assert.NotEmpty(candidates);
        Assert.All(candidates, p => Assert.EndsWith(@"Elder Scrolls Online\live\AddOns", p));
    }
}
