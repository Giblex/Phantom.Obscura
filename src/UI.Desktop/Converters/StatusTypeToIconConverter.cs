using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using PhantomVault.UI.Models;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts StatusType enum to appropriate SVG icon path
    /// </summary>
    public class StatusTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is StatusType statusType)
            {
                return statusType switch
                {
                    StatusType.Success => "avares://PhantomVault.UI/Assets/Visuals/App Icons/SVG/Info.svg",
                    StatusType.Warning => "avares://PhantomVault.UI/Assets/Visuals/App Icons/SVG/Warning.svg",
                    StatusType.Error => "avares://PhantomVault.UI/Assets/Visuals/App Icons/SVG/x.svg",
                    StatusType.Info => "avares://PhantomVault.UI/Assets/Visuals/App Icons/SVG/Info.svg",
                    _ => null
                };
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}
