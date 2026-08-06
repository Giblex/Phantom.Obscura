using System;
using System.Globalization;
using Avalonia.Collections;
using Avalonia.Data.Converters;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Converts a 0–100 progress value into a <c>StrokeDashArray</c> that draws the matching
    /// fraction of an ellipse's circumference as a single solid arc, so a ring can track a
    /// progress bar in sync. ConverterParameter format is "diameter|thickness" (e.g. "92|4").
    /// Avalonia dash units are multiples of stroke thickness, hence circumference/thickness.
    /// </summary>
    public class PercentToRingDashConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double percent = value switch
            {
                int i => i,
                double d => d,
                float f => f,
                long l => l,
                _ => 0
            };
            percent = Math.Clamp(percent, 0, 100);

            double diameter = 92, thickness = 4;
            if (parameter is string s)
            {
                var parts = s.Split('|');
                if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var dm) && dm > 0)
                    diameter = dm;
                if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var th) && th > 0)
                    thickness = th;
            }

            double circumferenceUnits = Math.PI * diameter / thickness;
            double drawn = circumferenceUnits * (percent / 100.0);
            if (drawn <= 0) drawn = 0.0001;
            double gap = Math.Max(circumferenceUnits - drawn, 0.0001);

            return new AvaloniaList<double> { drawn, gap };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
