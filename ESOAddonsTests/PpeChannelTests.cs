using EsoAddons.Services;
using Xunit;

namespace ESOAddons.Tests;

public class PpeChannelTests
{
    // A /releases list payload (newest first), mixing a pre-release and stable releases.
    private const string ReleasesJson = """
    [
      { "tag_name": "v0.3.1", "prerelease": true,  "draft": false, "html_url": "u31",
        "body": "ppe notes", "assets": [ { "name": "Shoyrus-ESO-Addons.exe", "browser_download_url": "exe31" } ] },
      { "tag_name": "v0.3.0", "prerelease": false, "draft": false, "html_url": "u30",
        "body": "prod notes", "assets": [ { "name": "Shoyrus-ESO-Addons.exe", "browser_download_url": "exe30" } ] }
    ]
    """;

    [Fact]
    public void Ppe_list_picks_highest_version_including_prerelease()
    {
        var info = UpdateChecker.ParseReleasesList(ReleasesJson, "0.3.0");
        Assert.NotNull(info);
        Assert.Equal("0.3.1", info!.Version);   // pre-release wins on PPE
        Assert.True(info.IsNewer);
        Assert.Equal("exe31", info.ExeUrl);
    }

    [Fact]
    public void Prod_latest_object_is_parsed_normally()
    {
        const string latest = """
        { "tag_name": "v0.3.0", "prerelease": false, "html_url": "u30", "body": "prod",
          "assets": [ { "name": "Shoyrus-ESO-Addons.exe", "browser_download_url": "exe30" } ] }
        """;
        var info = UpdateChecker.ParseLatestRelease(latest, "0.2.15");
        Assert.NotNull(info);
        Assert.Equal("0.3.0", info!.Version);
        Assert.True(info.IsNewer);
        Assert.Equal("exe30", info.ExeUrl);
    }

    [Fact]
    public void Aggregates_notes_for_every_version_behind_newest_first()
    {
        const string json = """
        [
          { "tag_name": "v0.3.5", "prerelease": false, "draft": false, "html_url": "u", "body": "notes five",
            "assets": [ { "name": "x.exe", "browser_download_url": "exe5" } ] },
          { "tag_name": "v0.3.4", "prerelease": false, "draft": false, "html_url": "u", "body": "notes four", "assets": [] },
          { "tag_name": "v0.3.3", "prerelease": false, "draft": false, "html_url": "u", "body": "notes three", "assets": [] }
        ]
        """;
        var info = UpdateChecker.ParseReleasesList(json, "0.3.2", includePrereleases: false);
        Assert.NotNull(info);
        Assert.Equal("0.3.5", info!.Version);          // target = highest
        Assert.Equal("exe5", info.ExeUrl);             // download = target's asset
        // all three versions present, newest first
        Assert.Contains("v0.3.5", info.Notes);
        Assert.Contains("notes five", info.Notes);
        Assert.Contains("v0.3.4", info.Notes);
        Assert.Contains("v0.3.3", info.Notes);
        Assert.True(info.Notes.IndexOf("v0.3.5") < info.Notes.IndexOf("v0.3.4"));
        Assert.True(info.Notes.IndexOf("v0.3.4") < info.Notes.IndexOf("v0.3.3"));
    }

    [Fact]
    public void Prod_aggregation_excludes_prereleases()
    {
        const string json = """
        [
          { "tag_name": "v0.4.0", "prerelease": true,  "draft": false, "html_url": "u", "body": "beta", "assets": [] },
          { "tag_name": "v0.3.5", "prerelease": false, "draft": false, "html_url": "u", "body": "stable five",
            "assets": [ { "name": "x.exe", "browser_download_url": "exe5" } ] }
        ]
        """;
        var info = UpdateChecker.ParseReleasesList(json, "0.3.4", includePrereleases: false);
        Assert.Equal("0.3.5", info!.Version);          // pre-release 0.4.0 ignored on PROD
        Assert.DoesNotContain("beta", info.Notes);
    }

    [Fact]
    public void Single_version_behind_shows_just_its_notes()
    {
        const string json = """
        [ { "tag_name": "v0.3.5", "prerelease": false, "draft": false, "html_url": "u", "body": "just five",
            "assets": [ { "name": "x.exe", "browser_download_url": "exe5" } ] } ]
        """;
        var info = UpdateChecker.ParseReleasesList(json, "0.3.4", includePrereleases: false);
        Assert.Equal("just five", info!.Notes);        // no aggregation headers when only one version behind
    }

    [Fact]
    public void Ppe_list_skips_drafts()
    {
        const string withDraft = """
        [
          { "tag_name": "v0.9.9", "prerelease": true, "draft": true, "html_url": "d", "body": "", "assets": [] },
          { "tag_name": "v0.3.0", "prerelease": false, "draft": false, "html_url": "u30", "body": "",
            "assets": [ { "name": "x.exe", "browser_download_url": "exe30" } ] }
        ]
        """;
        var info = UpdateChecker.ParseReleasesList(withDraft, "0.1.0");
        Assert.Equal("0.3.0", info!.Version);   // the draft 0.9.9 is ignored
    }
}
