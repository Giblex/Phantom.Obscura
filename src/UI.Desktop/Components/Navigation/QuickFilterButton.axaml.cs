using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Windows.Input;

namespace PhantomVault.UI.Components.Navigation;

/// <summary>
/// A reusable quick filter toggle button component with optional count badge.
/// Used in the sidebar for filter buttons like Passkeys, Recent, Expiring Soon, etc.
/// </summary>
public partial class QuickFilterButton : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<QuickFilterButton, string>(nameof(Label), "Filter");

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<QuickFilterButton, ICommand?>(nameof(Command));

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<QuickFilterButton, bool>(nameof(IsChecked), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<QuickFilterButton, int>(nameof(Count), 0);

    public static readonly StyledProperty<bool> ShowCountProperty =
        AvaloniaProperty.Register<QuickFilterButton, bool>(nameof(ShowCount), true);

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public bool ShowCount
    {
        get => GetValue(ShowCountProperty);
        set => SetValue(ShowCountProperty, value);
    }

    public QuickFilterButton()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
