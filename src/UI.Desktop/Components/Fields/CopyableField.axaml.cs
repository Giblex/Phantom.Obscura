using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Windows.Input;

namespace PhantomVault.UI.Components.Fields;

/// <summary>
/// A reusable field component with a label, read-only TextBox, and copy button.
/// Used throughout detail views for displaying copyable credential fields.
/// </summary>
public partial class CopyableField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<CopyableField, string>(nameof(Label), "Field");

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<CopyableField, string>(nameof(Value), string.Empty);

    public static readonly StyledProperty<ICommand?> CopyCommandProperty =
        AvaloniaProperty.Register<CopyableField, ICommand?>(nameof(CopyCommand));

    public static readonly StyledProperty<object?> CopyCommandParameterProperty =
        AvaloniaProperty.Register<CopyableField, object?>(nameof(CopyCommandParameter));

    public static readonly StyledProperty<string> ButtonTooltipProperty =
        AvaloniaProperty.Register<CopyableField, string>(nameof(ButtonTooltip), "Copy to clipboard");

    public static readonly StyledProperty<bool> IsPasswordProperty =
        AvaloniaProperty.Register<CopyableField, bool>(nameof(IsPassword), false);

    public static readonly StyledProperty<string> ButtonBackgroundProperty =
        AvaloniaProperty.Register<CopyableField, string>(nameof(ButtonBackground), "Transparent");

    public static readonly StyledProperty<string> ButtonClassesProperty =
        AvaloniaProperty.Register<CopyableField, string>(nameof(ButtonClasses), "secondary");

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ICommand? CopyCommand
    {
        get => GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public object? CopyCommandParameter
    {
        get => GetValue(CopyCommandParameterProperty);
        set => SetValue(CopyCommandParameterProperty, value);
    }

    public string ButtonTooltip
    {
        get => GetValue(ButtonTooltipProperty);
        set => SetValue(ButtonTooltipProperty, value);
    }

    public bool IsPassword
    {
        get => GetValue(IsPasswordProperty);
        set => SetValue(IsPasswordProperty, value);
    }

    public string ButtonBackground
    {
        get => GetValue(ButtonBackgroundProperty);
        set => SetValue(ButtonBackgroundProperty, value);
    }

    public string ButtonClasses
    {
        get => GetValue(ButtonClassesProperty);
        set => SetValue(ButtonClassesProperty, value);
    }

    public CopyableField()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsPasswordProperty)
        {
            var textBox = this.FindControl<TextBox>("ValueTextBox");
            if (textBox != null)
            {
                textBox.PasswordChar = IsPassword ? '•' : '\0';
            }
        }
        else if (change.Property == ButtonClassesProperty)
        {
            var button = this.FindControl<Button>("ActionButton");
            if (button != null && !string.IsNullOrWhiteSpace(ButtonClasses))
            {
                // Clear existing classes and add new ones
                button.Classes.Clear();
                foreach (var className in ButtonClasses.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    button.Classes.Add(className);
                }
            }
        }
    }
}
