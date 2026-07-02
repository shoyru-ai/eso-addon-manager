using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class VersionCompareTests
{
    [Theory]
    [InlineData("1.3.7", "1.3.6", true)]   // real upgrade
    [InlineData("1.3.6", "1.3.7", false)]  // installed newer
    [InlineData("1.0.0", "1.0", false)]    // 1.0 == 1.0.0
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("3.0.42", "3.0", true)]    // PvPMeter-style
    [InlineData("1.04", "3.01", false)]    // DKCorrosive-style: installed newer
    [InlineData("v5", "2", true)]          // strips 'v'
    [InlineData("2.0 r43", "2.0 r43", false)]
    [InlineData("2.0 r44", "2.0 r43", true)]
    public void IsNewer_compares_numerically(string latest, string installed, bool expected)
        => Assert.Equal(expected, VersionCompare.IsNewer(latest, installed));

    [Fact]
    public void IsNewer_false_when_unparseable_or_empty()
    {
        Assert.False(VersionCompare.IsNewer("abc", "def"));
        Assert.False(VersionCompare.IsNewer("", "1.0"));
        Assert.False(VersionCompare.IsNewer("1.0", null));
    }
}
