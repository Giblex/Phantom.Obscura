using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MatchType = PhantomVault.Core.Services.Autofill.MatchType;

namespace PhantomVault.UI.Converters
{

    public sealed class MatchTypeColorConverter : IValueConverter
    {
        public static readonly MatchTypeColorConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not MatchType matchType)
                return Brushes.Gray;

            return matchType switch
            {
                MatchType.Exact => new SolidColorBrush(Color.Parse("#4CAF50")),
                MatchType.Subdomain => new SolidColorBrush(Color.Parse("#2196F3")),
                MatchType.BaseDomain => new SolidColorBrush(Color.Parse("#808080")),
                _ => Brushes.Gray
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}

