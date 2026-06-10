using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Services
{

    public static class ThemeScope
    {

        public static readonly AttachedProperty<bool> IsThemedProperty =
            AvaloniaProperty.RegisterAttached<Window, bool>(
                "IsThemed",
                typeof(ThemeScope),
                defaultValue: false);

        public static bool GetIsThemed(Window window)
        {
            return window.GetValue(IsThemedProperty);
        }

        public static void SetIsThemed(Window window, bool value)
        {
            window.SetValue(IsThemedProperty, value);
        }
    }
}

