using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace EsoAddons.Behaviors;

/// <summary>
/// Attached property that loads a remote image URL into an Image asynchronously and reliably
/// (downloads the bytes, decodes from a stream, caches, and respects list virtualization reuse).
/// Usage: &lt;Image b:ImageLoader.SourceUrl="{Binding ThumbUrl}"/&gt;
/// </summary>
public static class ImageLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Dictionary<string, BitmapImage> Cache = new();

    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.RegisterAttached("SourceUrl", typeof(string), typeof(ImageLoader),
            new PropertyMetadata(null, OnSourceUrlChanged));

    public static void SetSourceUrl(DependencyObject o, string value) => o.SetValue(SourceUrlProperty, value);
    public static string GetSourceUrl(DependencyObject o) => (string)o.GetValue(SourceUrlProperty);

    private static async void OnSourceUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;
        var url = e.NewValue as string;
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            if (!Cache.TryGetValue(url, out var bmp))
            {
                var bytes = await Http.GetByteArrayAsync(url);
                bmp = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                Cache[url] = bmp;
            }
            // Guard against row recycling: only assign if this Image still wants this url.
            if (GetSourceUrl(image) == url) image.Source = bmp;
        }
        catch { /* leave blank on failure */ }
    }
}
