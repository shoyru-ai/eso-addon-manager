using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace EsoAddons.Converters;

/// <summary>Binds a remote image URL string to an Image.Source, downloading asynchronously.</summary>
public class UrlToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(url, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.DelayCreation;
            bi.EndInit();
            return bi;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
