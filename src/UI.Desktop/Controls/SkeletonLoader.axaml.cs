using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Skeleton loader control for displaying loading states.
/// Shows animated shimmer effect while content is loading.
/// </summary>
public partial class SkeletonLoader : UserControl
{
    private Border? _skeletonBorder;

    public static readonly StyledProperty<SkeletonType> TypeProperty =
        AvaloniaProperty.Register<SkeletonLoader, SkeletonType>(nameof(Type), defaultValue: SkeletonType.Text);

    public SkeletonType Type
    {
        get => GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    public SkeletonLoader()
    {
        InitializeComponent();
        _skeletonBorder = this.FindControl<Border>("SkeletonBorder");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TypeProperty && _skeletonBorder != null)
        {
            UpdateSkeletonType();
        }
    }

    private void UpdateSkeletonType()
    {
        if (_skeletonBorder == null)
            return;

        _skeletonBorder.Classes.Clear();
        _skeletonBorder.Classes.Add("skeleton");

        switch (Type)
        {
            case SkeletonType.Text:
                _skeletonBorder.Classes.Add("text");
                break;
            case SkeletonType.Title:
                _skeletonBorder.Classes.Add("title");
                break;
            case SkeletonType.Circle:
                _skeletonBorder.Classes.Add("circle");
                break;
            case SkeletonType.Card:
                _skeletonBorder.Classes.Add("card");
                break;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// Type of skeleton loader shape.
/// </summary>
public enum SkeletonType
{
    Text,
    Title,
    Circle,
    Card
}
