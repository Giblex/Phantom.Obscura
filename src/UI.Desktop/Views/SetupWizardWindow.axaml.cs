using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;

namespace PhantomVault.UI.Views
{
    /// <summary>
    /// First-time setup wizard window that guides users through vault creation.
    /// </summary>
    public partial class SetupWizardWindow : ThemeAwareWindow
    {
        private bool _entropyLeftPressed;
        private bool _entropyRightPressed;

        public SetupWizardWindow()
        {
            // Fixed dark navy — pre-vault screens never follow user theme
            ThemeScope.SetIsThemed(this, false);
            InitializeComponent();
            var viewModel = new SetupWizardViewModel();
            viewModel.SetOwnerWindow(this);
            DataContext = viewModel;
            viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        public SetupWizardWindow(SetupWizardViewModel viewModel)
        {
            // Fixed dark navy — pre-vault screens never follow user theme
            ThemeScope.SetIsThemed(this, false);
            InitializeComponent();
            viewModel.SetOwnerWindow(this);
            DataContext = viewModel;
            viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SetupWizardViewModel.CurrentStep))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    MainScrollViewer.Offset = new Vector(0, 0);
                }, DispatcherPriority.Render);
            }
        }

        private void SecurityLevelCard_Tapped(object? sender, TappedEventArgs e)
        {
            if (sender is Control control
                && control.DataContext is SecurityLevelOption option
                && DataContext is SetupWizardViewModel vm)
            {
                vm.SelectedSecurityLevel = option.Name;
            }
        }

        private void EntropyCaptureSurface_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (sender is not Control control || DataContext is not SetupWizardViewModel vm)
                return;

            var point = e.GetPosition(control);
            vm.RecordEntropyPointerSample(point.X, point.Y, _entropyLeftPressed, _entropyRightPressed);
        }

        private void EntropyCaptureSurface_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            UpdateEntropyButtonState(e.GetCurrentPoint(sender as Visual));
            EntropyCaptureSurface_PointerMoved(sender, e);
        }

        private void EntropyCaptureSurface_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            UpdateEntropyButtonState(e.GetCurrentPoint(sender as Visual));
            EntropyCaptureSurface_PointerMoved(sender, e);
        }

        private void UpdateEntropyButtonState(PointerPoint point)
        {
            _entropyLeftPressed = point.Properties.IsLeftButtonPressed;
            _entropyRightPressed = point.Properties.IsRightButtonPressed;
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
            => BindingOperations.DoNothing;
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
                    return new SolidColorBrush(Color.Parse("#173042"));
                }
                else if (currentStep == targetStep)
                {
                    return new SolidColorBrush(Color.Parse("#1F3F55"));
                }
                else
                {
                    return new SolidColorBrush(Color.Parse("#152434"));
                }
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
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
                ? new SolidColorBrush(Color.Parse("#111B2A"))
                : new SolidColorBrush(Color.Parse("#20FFFFFF"));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
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
            => BindingOperations.DoNothing;
    }

    /// <summary>
    /// Converts current step and target step to determine if step is clickable (completed steps only).
    /// </summary>
    public class StepClickableConverter : IValueConverter
    {
        public static readonly StepClickableConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int currentStep && parameter is string stepParam && int.TryParse(stepParam, out int targetStep))
            {
                // Can click completed steps (before current step)
                return currentStep > targetStep;
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }

    /// <summary>
    /// Converts current step to cursor for clickable steps.
    /// </summary>
    public class StepCursorConverter : IValueConverter
    {
        public static readonly StepCursorConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int currentStep && parameter is string stepParam && int.TryParse(stepParam, out int targetStep))
            {
                // Completed steps get Hand cursor
                return currentStep > targetStep ? Avalonia.Input.StandardCursorType.Hand : Avalonia.Input.StandardCursorType.Arrow;
            }
            return Avalonia.Input.StandardCursorType.Arrow;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }
}
