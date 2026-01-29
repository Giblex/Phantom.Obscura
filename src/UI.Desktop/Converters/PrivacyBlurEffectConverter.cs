using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{
    public sealed class PrivacyBlurEffectConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool enabled && enabled)
            {
                var radius = 10.0;

                if (parameter is double doubleRadius)
                {
                    radius = doubleRadius;
                }
                else if (parameter is string radiusText && double.TryParse(radiusText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius))
                {
                    radius = parsedRadius;
                }

                return new BlurEffect { Radius = radius };
            }

            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
