using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhantomVault.UI.Converters
{
    /// <summary>
    /// Returns a muted grey brush when the category IsTrash, otherwise transparent.
    /// Used to visually distinguish the locked rubbish-bin tile.
    /// </summary>
    public sealed class IsTrashToBrushConverter : IValueConverter
    {
        public static readonly IsTrashToBrushConverter Instance = new();

        private static readonly IBrush TrashBrush = new SolidColorBrush(Color.Parse("#2A3040"));
        private static readonly IBrush NormalBrush = Brushes.Transparent;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isTrash && isTrash)
                return TrashBrush;
            return NormalBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
