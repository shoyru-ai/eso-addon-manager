using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class BBCodeTests
{
    [Fact]
    public void Strips_formatting_tags()
        => Assert.Equal("Hello world", BBCode.ToText("[B]Hello[/B] [COLOR=RED]world[/COLOR]"));

    [Fact]
    public void Strips_size_and_nested_tags()
        => Assert.Equal("Big bold", BBCode.ToText("[SIZE=5][B]Big bold[/B][/SIZE]"));

    [Fact]
    public void Url_with_label_keeps_label_and_target()
        => Assert.Equal("LuiMedia (https://x/LuiMedia.html)",
            BBCode.ToText("[URL=https://x/LuiMedia.html]LuiMedia[/URL]"));

    [Fact]
    public void Bare_url_is_kept()
        => Assert.Equal("https://x/y", BBCode.ToText("[URL]https://x/y[/URL]"));

    [Fact]
    public void Lists_become_bullets()
    {
        var r = BBCode.ToText("[LIST][*]One[*]Two[/LIST]");
        Assert.Contains("• One", r);
        Assert.Contains("• Two", r);
    }

    [Fact]
    public void Youtube_becomes_placeholder()
        => Assert.Equal("(video)", BBCode.ToText("[youtube]abc123[/youtube]"));

    [Fact]
    public void Null_or_empty_returns_empty()
    {
        Assert.Equal("", BBCode.ToText(null));
        Assert.Equal("", BBCode.ToText(""));
        Assert.Equal("", BBCode.ToText("   "));
    }

    [Fact]
    public void Collapses_excess_blank_lines()
        => Assert.Equal("a\n\nb", BBCode.ToText("a\n\n\n\n\nb"));
}
