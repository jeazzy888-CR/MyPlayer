using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace MyPlayer_NET9.Converters
{
    /// <summary>
    /// Показывает элемент, только если значение enum-свойства совпадает
    /// с одним из значений в ConverterParameter (через запятую, если их
    /// несколько — например "Songs,AlbumsGrid"). Используется для
    /// переключения "экранов" (Список треков / Сетка альбомов / Детали альбома)
    /// внутри одного и того же Grid — без отдельного окна или Frame-навигации.
    /// </summary>
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return Visibility.Collapsed;

            var allowedValues = parameter.ToString()!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return allowedValues.Contains(value.ToString(), StringComparer.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
