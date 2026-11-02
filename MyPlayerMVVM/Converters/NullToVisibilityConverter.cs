using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyPlayer_NET9.Converters
{
    /// <summary>Видим, когда значение null — показывает серую заглушку там, где нет обложки.</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
