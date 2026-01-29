using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Calculates the overlay margin for the quick filter highlight so it glides to the active toggle.
    /// </summary>
    public class QuickFilterHighlightMarginConverter : IMultiValueConverter
    {
        /// <summary>
        /// Height of each quick filter button.
        /// </summary>
        public double ButtonHeight { get; set; } = 36d;

        /// <summary>
        /// Vertical spacing between buttons.
        /// </summary>
        public double Spacing { get; set; } = 6d;

        /// <summary>
        /// Offset applied before the first button (useful when the overlay needs padding from the container top).
        /// </summary>
        public double BaseOffset { get; set; } = 0d;

        /// <summary>
        /// Horizontal padding applied on both sides of the overlay.
        /// </summary>
        public double HorizontalPadding { get; set; } = 6d;

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is not { Count: > 0 })
            {
                return new Thickness(0);
            }

            var index = 0;
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] is bool isActive && isActive)
                {
                    index = i;
                    break;
                }
            }

            var top = BaseOffset + index * (ButtonHeight + Spacing);
            return new Thickness(HorizontalPadding, top, HorizontalPadding, 0);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }
}
