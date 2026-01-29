using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PhantomVault.UI.Controls
{
    /// <summary>
    /// Reusable hero header component with gradient background,
    /// pill badge, large title, subtitle, and optional glass card panel.
    /// </summary>
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

        /// <summary>
        /// Text displayed in the pill badge (e.g., "Trusted delivery channel").
        /// </summary>
        public string? BadgeText
        {
            get => GetValue(BadgeTextProperty);
            set => SetValue(BadgeTextProperty, value);
        }

        /// <summary>
        /// Main hero title (displayed large, uppercase style).
        /// </summary>
        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>
        /// Subtitle text below the title.
        /// </summary>
        public string? Subtitle
        {
            get => GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        /// <summary>
        /// Content for the actions area (buttons, links, etc.).
        /// </summary>
        public object? ActionsContent
        {
            get => GetValue(ActionsContentProperty);
            set => SetValue(ActionsContentProperty, value);
        }

        /// <summary>
        /// Whether to show the glass panel on the right side.
        /// </summary>
        public bool ShowGlassPanel
        {
            get => GetValue(ShowGlassPanelProperty);
            set => SetValue(ShowGlassPanelProperty, value);
        }

        /// <summary>
        /// Title for the glass panel.
        /// </summary>
        public string? GlassPanelTitle
        {
            get => GetValue(GlassPanelTitleProperty);
            set => SetValue(GlassPanelTitleProperty, value);
        }

        /// <summary>
        /// Content for the glass panel.
        /// </summary>
        public object? GlassPanelContent
        {
            get => GetValue(GlassPanelContentProperty);
            set => SetValue(GlassPanelContentProperty, value);
        }
    }
}
