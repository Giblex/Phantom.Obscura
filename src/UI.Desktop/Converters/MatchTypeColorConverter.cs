using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MatchType = PhantomVault.Core.Services.Autofill.MatchType;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts MatchType to a color brush for visual indication.
    /// </summary>
    public sealed class MatchTypeColorConverter : IValueConverter
    {
        public static readonly MatchTypeColorConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not MatchType matchType)
                return Brushes.Gray;

            return matchType switch
            {
                MatchType.Exact => new SolidColorBrush(Color.Parse("#4CAF50")), // Green
                MatchType.Subdomain => new SolidColorBrush(Color.Parse("#2196F3")), // Blue
                MatchType.BaseDomain => new SolidColorBrush(Color.Parse("#808080")), // Gray
                _ => Brushes.Gray
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
