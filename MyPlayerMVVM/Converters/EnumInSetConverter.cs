using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace MyPlayer_NET9.Converters
{
    /// <summary>
    /// true, если значение enum совпадает с одним из значений в ConverterParameter (через запятую).
    /// То же, что EnumToVisibilityConverter, но возвращает bool — для «активной вкладки» в боковой панели.
    /// </summary>
    public class EnumInSetConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;

            return parameter.ToString()!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(value.ToString(), StringComparer.Ordinal);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
