using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class UpdateCheckerTests
{
    private const string Sample = """
    {
      "tag_name": "v0.2.0",
      "html_url": "https://github.com/shoyru-ai/eso-addon-manager/releases/tag/v0.2.0",
      "body": "- Fixed sorting\n- Added changelog",
      "assets": [
        { "name": "Shoyrus-ESO-Addons.exe", "browser_download_url": "https://example/app.exe" },
        { "name": "ESO-Addons-v0.2.0.zip",  "browser_download_url": "https://example/app.zip" }
      ]
    }
    """;

    [Fact]
    public void Detects_newer_version_and_assets()
    {
        var info = UpdateChecker.ParseLatestRelease(Sample, "0.1.0");
        Assert.NotNull(info);
        Assert.True(info!.IsNewer);
        Assert.Equal("0.2.0", info.Version);
        Assert.Equal("https://example/app.exe", info.ExeUrl);
        Assert.Equal("https://example/app.zip", info.ZipUrl);
        Assert.Contains("Fixed sorting", info.Notes);
        Assert.Equal("https://github.com/shoyru-ai/eso-addon-manager/releases/tag/v0.2.0", info.ReleaseUrl);
    }

    [Fact]
    public void Not_newer_when_versions_equal()
        => Assert.False(UpdateChecker.ParseLatestRelease(Sample, "0.2.0")!.IsNewer);

    [Fact]
    public void Not_newer_when_current_is_ahead()
        => Assert.False(UpdateChecker.ParseLatestRelease(Sample, "0.3.0")!.IsNewer);

    [Fact]
    public void Returns_info_even_without_assets()
    {
        var json = """{ "tag_name": "v1.0.0", "html_url": "https://x", "body": "" }""";
        var info = UpdateChecker.ParseLatestRelease(json, "0.1.0");
        Assert.NotNull(info);
        Assert.True(info!.IsNewer);
        Assert.Equal("", info.ExeUrl);
    }

    [Fact]
    public void Null_when_no_tag()
        => Assert.Null(UpdateChecker.ParseLatestRelease("""{ "name": "no tag here" }""", "0.1.0"));
}
