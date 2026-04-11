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
                    StatusType.Success => "avares://PhantomVault.UI/Assets/SVG/success-icon.svg",
                    StatusType.Warning => "avares://PhantomVault.UI/Assets/SVG/warning-icon.svg",
                    StatusType.Error => "avares://PhantomVault.UI/Assets/SVG/close-icon.svg",
                    StatusType.Info => "avares://PhantomVault.UI/Assets/SVG/info-icon.svg",
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
