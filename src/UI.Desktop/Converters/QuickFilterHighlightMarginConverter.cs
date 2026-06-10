using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{

    public class QuickFilterHighlightMarginConverter : IMultiValueConverter
    {

        public double ButtonHeight { get; set; } = 36d;

        public double Spacing { get; set; } = 6d;

        public double BaseOffset { get; set; } = 0d;

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

