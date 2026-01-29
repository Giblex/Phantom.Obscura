using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhantomVault.Core.Services.Autofill;

namespace PhantomVault.UI.Controls
{
    /// <summary>
    /// Visual overlay that highlights an autofillable input field.
    /// </summary>
    public sealed class FieldOverlay : Control
    {
        public static readonly StyledProperty<BoundingBox?> TargetFieldProperty =
            AvaloniaProperty.Register<FieldOverlay, BoundingBox?>(nameof(TargetField));

        public static readonly StyledProperty<bool> IsActiveProperty =
            AvaloniaProperty.Register<FieldOverlay, bool>(nameof(IsActive));

        public static readonly StyledProperty<FormFieldType> FieldTypeProperty =
            AvaloniaProperty.Register<FieldOverlay, FormFieldType>(nameof(FieldType));

        private const double IconSize = 20;
        private const double BorderWidth = 2;

        public FieldOverlay()
        {
            IsHitTestVisible = false; // Allow clicks to pass through
        }

        public BoundingBox? TargetField
        {
            get => GetValue(TargetFieldProperty);
            set => SetValue(TargetFieldProperty, value);
        }

        public bool IsActive
        {
            get => GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public FormFieldType FieldType
        {
            get => GetValue(FieldTypeProperty);
            set => SetValue(FieldTypeProperty, value);
        }

        static FieldOverlay()
        {
            AffectsRender<FieldOverlay>(TargetFieldProperty, IsActiveProperty, FieldTypeProperty);
        }

        public override void Render(DrawingContext context)
        {
            if (TargetField == null || !IsActive)
                return;

            var field = TargetField;
            var rect = new Rect(field.X, field.Y, field.Width, field.Height);

            // Draw border around field
            var borderBrush = GetBorderBrush();
            var pen = new Pen(borderBrush, BorderWidth);
            context.DrawRectangle(null, pen, rect, 4);

            // Draw icon in top-right corner
            var iconRect = new Rect(
                field.X + field.Width - IconSize - 4,
                field.Y + 4,
                IconSize,
                IconSize);

            // Icon background
            context.DrawRectangle(
                new SolidColorBrush(Color.Parse("#5865F2")),
                null,
                iconRect,
                IconSize / 2);

            // Icon (simplified key symbol)
            var iconBrush = Brushes.White;
            var iconPen = new Pen(iconBrush, 2);
            
            var keyCircleCenter = new Point(iconRect.Center.X, iconRect.Center.Y - 2);
            context.DrawEllipse(null, iconPen, keyCircleCenter, 3, 3);
            
            var lineStart = new Point(keyCircleCenter.X, keyCircleCenter.Y + 3);
            var lineEnd = new Point(keyCircleCenter.X, iconRect.Bottom - 4);
            context.DrawLine(iconPen, lineStart, lineEnd);
        }

        private IBrush GetBorderBrush()
        {
            if (!IsActive)
                return Brushes.Transparent;

            return FieldType switch
            {
                FormFieldType.Password => new SolidColorBrush(Color.Parse("#5865F2")), // Blue
                FormFieldType.Username or FormFieldType.Email => new SolidColorBrush(Color.Parse("#4CAF50")), // Green
                FormFieldType.TwoFactor => new SolidColorBrush(Color.Parse("#FF9800")), // Orange
                FormFieldType.Passkey => new SolidColorBrush(Color.Parse("#9C27B0")), // Purple
                _ => new SolidColorBrush(Color.Parse("#5865F2")) // Default blue
            };
        }
    }
}
