using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Attached property to mark windows as themed or unthemed.
    /// Only windows with IsThemed=true will receive runtime theme updates.
    /// Welcome/Setup windows should have IsThemed=false to remain unchanged.
    /// </summary>
    public static class ThemeScope
    {
        /// <summary>
        /// Attached property indicating whether a window participates in runtime theme switching.
        /// Default is false (window is NOT themed).
        /// </summary>
        public static readonly AttachedProperty<bool> IsThemedProperty =
            AvaloniaProperty.RegisterAttached<Window, bool>(
                "IsThemed",
                typeof(ThemeScope),
                defaultValue: false);

        /// <summary>
        /// Gets whether the window is themed.
        /// </summary>
        public static bool GetIsThemed(Window window)
        {
            return window.GetValue(IsThemedProperty);
        }

        /// <summary>
        /// Sets whether the window is themed.
        /// </summary>
        public static void SetIsThemed(Window window, bool value)
        {
            window.SetValue(IsThemedProperty, value);
        }
    }
}
