using System.Net;
using System.Text.RegularExpressions;

namespace EsoAddons.Services;

/// <summary>Converts ESOUI BBCode descriptions into readable plain text.</summary>
public static partial class BBCode
{
    public static string ToText(string? bb)
    {
        if (string.IsNullOrWhiteSpace(bb)) return "";
        var s = bb;

        // Links: [URL=x]label[/URL] -> "label (x)" ; [URL]x[/URL] -> "x"
        s = UrlLabel().Replace(s, m => $"{m.Groups[2].Value} ({m.Groups[1].Value})");
        s = UrlBare().Replace(s, "$1");
        // Media
        s = Img().Replace(s, "");
        s = YouTube().Replace(s, "(video)");
        // Lists
        s = Regex.Replace(s, @"\[\*\]", "\n• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[/?list[^\]]*\]", "\n", RegexOptions.IgnoreCase);
        // Strip every remaining [tag] / [/tag] / [tag=...]
        s = AnyTag().Replace(s, "");
        // HTML entities that sometimes appear
        s = WebUtility.HtmlDecode(s);
        // Collapse excess whitespace
        s = Regex.Replace(s, @"[ \t]+\n", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    [GeneratedRegex(@"\[url=([^\]]+)\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UrlLabel();
    [GeneratedRegex(@"\[url\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UrlBare();
    [GeneratedRegex(@"\[img\].*?\[/img\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Img();
    [GeneratedRegex(@"\[youtube\].*?\[/youtube\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex YouTube();
    [GeneratedRegex(@"\[/?[a-zA-Z][^\]]*\]")]
    private static partial Regex AnyTag();
}
