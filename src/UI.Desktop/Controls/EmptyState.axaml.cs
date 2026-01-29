using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Empty state control for displaying friendly messages when no data is available.
/// Includes illustration, title, description, and optional action button.
/// </summary>
public partial class EmptyState : UserControl
{
    private TextBlock? _titleText;
    private TextBlock? _descriptionText;
    private Button? _actionButton;

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Title), defaultValue: "No items");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Description), defaultValue: "There's nothing here yet.");

    public static readonly StyledProperty<string> ActionTextProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(ActionText), defaultValue: "Get Started");

    public static readonly StyledProperty<bool> ShowActionProperty =
        AvaloniaProperty.Register<EmptyState, bool>(nameof(ShowAction), defaultValue: true);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public bool ShowAction
    {
        get => GetValue(ShowActionProperty);
        set => SetValue(ShowActionProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? ActionClicked;

    public EmptyState()
    {
        InitializeComponent();

        _titleText = this.FindControl<TextBlock>("TitleText");
        _descriptionText = this.FindControl<TextBlock>("DescriptionText");
        _actionButton = this.FindControl<Button>("ActionButton");

        if (_actionButton != null)
        {
            _actionButton.Click += OnActionButtonClick;
        }

        UpdateContent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty ||
            change.Property == DescriptionProperty ||
            change.Property == ActionTextProperty ||
            change.Property == ShowActionProperty)
        {
            UpdateContent();
        }
    }

    private void UpdateContent()
    {
        if (_titleText != null)
            _titleText.Text = Title;

        if (_descriptionText != null)
            _descriptionText.Text = Description;

        if (_actionButton != null)
        {
            _actionButton.Content = ActionText;
            _actionButton.IsVisible = ShowAction;
        }
    }

    private void OnActionButtonClick(object? sender, RoutedEventArgs e)
    {
        ActionClicked?.Invoke(this, e);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
