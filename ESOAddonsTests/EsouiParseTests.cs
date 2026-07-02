using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class EsouiParseTests
{
    [Fact]
    public void ParseCatalog_maps_ui_prefixed_fields()
    {
        var json = """
        [{"UID":"818","UIName":"LuiExtended","UIAuthorName":"psypanda","UIVersion":"7.2.5",
          "UIDownloadTotal":12345,"UIDate":1700000000000,"UIFileInfoURL":"https://esoui/info818",
          "UICATID":"19","UIDir":["LuiExtended"],
          "UIIMG_Thumbs":["https://cdn/preview/tiny/pvw.png"],"UIIMGs":["https://cdn/preview/pvw.png"]}]
        """;
        var a = Assert.Single(EsouiClient.ParseCatalog(json));
        Assert.Equal("818", a.Id);
        Assert.Equal("LuiExtended", a.Title);
        Assert.Equal("psypanda", a.Author);
        Assert.Equal("7.2.5", a.Version);
        Assert.Equal(12345, a.Downloads);
        Assert.Equal(1700000000000, a.LastUpdateMs);
        Assert.Equal(new[] { "LuiExtended" }, a.Dirs);
        Assert.Equal("https://cdn/preview/tiny/pvw.png", a.ThumbUrl);   // tiny, for list rows
        Assert.Equal("https://cdn/preview/pvw.png", a.ImageUrl);        // full, for detail + enlarge
        Assert.False(a.IsLibrary);
    }

    [Fact]
    public void ParseCatalog_flags_library_category_53()
    {
        var json = """[{"UID":"44","UIName":"LibStub","UICATID":"53","UIDir":["LibStub"]}]""";
        Assert.True(Assert.Single(EsouiClient.ParseCatalog(json)).IsLibrary);
    }

    [Fact]
    public void ParseCatalog_handles_string_or_numeric_fields()
    {
        // downloads as string, UID as number
        var json = """[{"UID":7,"UIName":"X","UIDownloadTotal":"999"}]""";
        var a = Assert.Single(EsouiClient.ParseCatalog(json));
        Assert.Equal("7", a.Id);
        Assert.Equal(999, a.Downloads);
    }

    [Fact]
    public void ParseDetails_extracts_download_dirs_and_text()
    {
        var json = """
        [{"UID":"7","UIVersion":"2.0 r43","UIDownload":"https://cdn/getfile?id=7",
          "UIDir":["LibAddonMenu-2.0"],"UIDescription":"[B]Hi[/B]","UIChangeLog":"[B]log[/B]",
          "UIIMGs":["https://i/full.png"]}]
        """;
        var d = EsouiClient.ParseDetails(json);
        Assert.Equal("https://cdn/getfile?id=7", d.DownloadUrl);
        Assert.Equal("2.0 r43", d.Version);
        Assert.Equal(new[] { "LibAddonMenu-2.0" }, d.Dirs);
        Assert.Equal("Hi", d.Description);
        Assert.Equal("log", d.ChangeLog);
        Assert.Equal("https://i/full.png", d.ImageUrl);
    }

    [Fact]
    public void ParseDetails_throws_when_no_download_url()
        => Assert.Throws<InvalidOperationException>(() => EsouiClient.ParseDetails("""[{"UID":"7"}]"""));

    [Fact]
    public void ParseCategories_maps_and_skips_empty_titles()
    {
        var json = """
        [{"UICATID":"19","UICATTitle":"Action Bar Mods","UICATFileCount":42},
         {"UICATID":"99","UICATTitle":"","UICATFileCount":0}]
        """;
        var c = Assert.Single(EsouiClient.ParseCategories(json));
        Assert.Equal("19", c.Id);
        Assert.Equal("Action Bar Mods", c.Title);
        Assert.Equal(42, c.Count);
    }
}
