using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    // Returns true when the provided width (double) is less than the ConverterParameter (threshold)
    public sealed class IsWidthLessThanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                double threshold = 1200;
                if (parameter != null)
                {
                    if (parameter is double d) threshold = d;
                    else if (double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                        threshold = parsed;
                }
                return width < threshold;
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
