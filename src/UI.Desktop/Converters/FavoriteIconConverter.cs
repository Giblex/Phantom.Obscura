using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{

    public class FavoriteIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isFavorite = value is bool b && b;
            return isFavorite
                ? "avares://PhantomVault.UI/Assets/Visuals/App Icons/SVG/favourites-active-icon.svg"
                : "avares://PhantomVault.UI/Assets/Visuals/App Icons/SVG/favourites-icon.svg";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}

