using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Controls
{

    public partial class HeroHeader : UserControl
    {
        public static readonly StyledProperty<string?> BadgeTextProperty =
            AvaloniaProperty.Register<HeroHeader, string?>(nameof(BadgeText));

        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<HeroHeader, string?>(nameof(Title));

        public static readonly StyledProperty<string?> SubtitleProperty =
            AvaloniaProperty.Register<HeroHeader, string?>(nameof(Subtitle));

        public static readonly StyledProperty<object?> ActionsContentProperty =
            AvaloniaProperty.Register<HeroHeader, object?>(nameof(ActionsContent));

        public static readonly StyledProperty<bool> ShowGlassPanelProperty =
            AvaloniaProperty.Register<HeroHeader, bool>(nameof(ShowGlassPanel), defaultValue: false);

        public static readonly StyledProperty<string?> GlassPanelTitleProperty =
            AvaloniaProperty.Register<HeroHeader, string?>(nameof(GlassPanelTitle));

        public static readonly StyledProperty<object?> GlassPanelContentProperty =
            AvaloniaProperty.Register<HeroHeader, object?>(nameof(GlassPanelContent));

        public HeroHeader()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public string? BadgeText
        {
            get => GetValue(BadgeTextProperty);
            set => SetValue(BadgeTextProperty, value);
        }

        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string? Subtitle
        {
            get => GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public object? ActionsContent
        {
            get => GetValue(ActionsContentProperty);
            set => SetValue(ActionsContentProperty, value);
        }

        public bool ShowGlassPanel
        {
            get => GetValue(ShowGlassPanelProperty);
            set => SetValue(ShowGlassPanelProperty, value);
        }

        public string? GlassPanelTitle
        {
            get => GetValue(GlassPanelTitleProperty);
            set => SetValue(GlassPanelTitleProperty, value);
        }

        public object? GlassPanelContent
        {
            get => GetValue(GlassPanelContentProperty);
            set => SetValue(GlassPanelContentProperty, value);
        }
    }
}

