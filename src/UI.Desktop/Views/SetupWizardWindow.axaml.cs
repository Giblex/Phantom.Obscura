using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// First-time setup wizard window that guides users through vault creation.
    /// </summary>
    public partial class SetupWizardWindow : ThemeAwareWindow
    {
        public SetupWizardWindow()
        {
            InitializeComponent();
            DataContext = new SetupWizardViewModel();
        }

        public SetupWizardWindow(SetupWizardViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }

    /// <summary>
    /// Converts step number to visibility (true if current step matches).
    /// </summary>
    public class StepVisibilityConverter : IValueConverter
    {
        public static readonly StepVisibilityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int currentStep && parameter is string stepParam && int.TryParse(stepParam, out int targetStep))
            {
                return currentStep == targetStep;
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts step number to progress indicator background (completed, current, or pending).
    /// </summary>
    public class StepProgressConverter : IValueConverter
    {
        public static readonly StepProgressConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int currentStep && parameter is string stepParam && int.TryParse(stepParam, out int targetStep))
            {
                if (currentStep > targetStep)
                {
                    // Completed step - solid accent
                    return new SolidColorBrush(Color.Parse("#6B8CAE"));
                }
                else if (currentStep == targetStep)
                {
                    // Current step - highlighted
                    return new SolidColorBrush(Color.Parse("#8FB5DF"));
                }
                else
                {
                    // Pending step - transparent
                    return new SolidColorBrush(Color.Parse("#20FFFFFF"));
                }
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts security level name to selected state.
    /// </summary>
    public class SecurityLevelSelectedConverter : IValueConverter
    {
        public static readonly SecurityLevelSelectedConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // This would need to be bound to the ViewModel's selected level
            return value?.ToString() == "Balanced"; // Default selection
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts storage location to radio button selection.
    /// </summary>
    public class StorageLocationConverter : IValueConverter
    {
        public static readonly StorageLocationConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true && parameter is string location)
            {
                return location;
            }
            return "USB";
        }
    }

    /// <summary>
    /// Converts storage location to border highlight.
    /// </summary>
    public class StorageLocationBorderConverter : IValueConverter
    {
        public static readonly StorageLocationBorderConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isSelected = value?.ToString() == parameter?.ToString();
            return isSelected 
                ? new SolidColorBrush(Color.Parse("#6B8CAE"))
                : new SolidColorBrush(Color.Parse("#20FFFFFF"));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts password strength to progress value.
    /// </summary>
    public class PasswordStrengthValueConverter : IValueConverter
    {
        public static readonly PasswordStrengthValueConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value?.ToString() switch
            {
                "None" => 0,
                "Weak" => 20,
                "Fair" => 40,
                "Good" => 70,
                "Excellent" => 100,
                _ => 0
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts password strength to color.
    /// </summary>
    public class PasswordStrengthColorConverter : IValueConverter
    {
        public static readonly PasswordStrengthColorConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value?.ToString() switch
            {
                "None" => new SolidColorBrush(Color.Parse("#666666")),
                "Weak" => new SolidColorBrush(Color.Parse("#E53935")),
                "Fair" => new SolidColorBrush(Color.Parse("#FB8C00")),
                "Good" => new SolidColorBrush(Color.Parse("#7CB342")),
                "Excellent" => new SolidColorBrush(Color.Parse("#43A047")),
                _ => new SolidColorBrush(Color.Parse("#666666"))
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts password match to icon.
    /// </summary>
    public class PasswordMatchIconConverter : IValueConverter
    {
        public static readonly PasswordMatchIconConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? "✓" : "✗";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts password match to text.
    /// </summary>
    public class PasswordMatchTextConverter : IValueConverter
    {
        public static readonly PasswordMatchTextConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? "Passwords match" : "Passwords do not match";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts password match to color.
    /// </summary>
    public class PasswordMatchColorConverter : IValueConverter
    {
        public static readonly PasswordMatchColorConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true
                ? new SolidColorBrush(Color.Parse("#43A047"))
                : new SolidColorBrush(Color.Parse("#E53935"));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts bool to "Enabled/Disabled" text.
    /// </summary>
    public class BoolToEnabledConverter : IValueConverter
    {
        public static readonly BoolToEnabledConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? "Enabled" : "Not configured";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts bool to "Will be generated/Not used" text.
    /// </summary>
    public class BoolToGeneratedConverter : IValueConverter
    {
        public static readonly BoolToGeneratedConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? "Will be generated" : "Not used";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts bool to status background color (green for available, orange for unavailable).
    /// </summary>
    public class BoolToStatusBackgroundConverter : IValueConverter
    {
        public static readonly BoolToStatusBackgroundConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool available = value is true;
            return available
                ? new SolidColorBrush(Color.Parse("#2D4A3A")) // Green-ish
                : new SolidColorBrush(Color.Parse("#4A3A2D")); // Orange-ish
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts bool to check/cross icon.
    /// </summary>
    public class BoolToCheckConverter : IValueConverter
    {
        public static readonly BoolToCheckConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? "✓" : "✗";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
