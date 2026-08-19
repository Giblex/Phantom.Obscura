using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views
{

    public class ThemeAwareWindow : Window
    {

        private const string ReduceMotionClass = "reduce-motion";
        private const string ReduceTransparencyClass = "reduce-transparency";
        private const string FlatButtonsClass = "flat-buttons";
        private const string FlatButtonBordersClass = "flat-button-borders";

        private EventHandler? _accessibilityHandler;
        private bool _dotBackdropApplied;

        public ThemeAwareWindow()
        {

            ThemeScope.SetIsThemed(this, true);

            Opened += OnOpened;
            Closed += OnClosed;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            RegisterAndApplyTheme();
            ApplyAccentResources();
            ApplyAccessibilityClasses();
            ApplyButtonPresentationClasses();
            ApplyGiblexDotBackdrop();

            _accessibilityHandler = (_, __) => ApplyAccessibilityClasses();
            AccessibilityService.Instance.SettingsChanged += _accessibilityHandler;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_accessibilityHandler is not null)
            {
                AccessibilityService.Instance.SettingsChanged -= _accessibilityHandler;
                _accessibilityHandler = null;
            }

            UnregisterFromThemeService();
            Opened -= OnOpened;
            Closed -= OnClosed;
        }

        private void ApplyAccessibilityClasses()
        {
            try
            {
                ToggleClass(ReduceMotionClass, AccessibilityService.Instance.ReduceMotion);
                ToggleClass(ReduceTransparencyClass, AccessibilityService.Instance.ReduceTransparency);
            }
            catch
            {

            }
        }

        private void ApplyButtonPresentationClasses()
        {
            try
            {
                var resources = Application.Current?.Resources;
                ToggleClass(FlatButtonsClass,
                    resources?["UseFlatButtons"] is bool useFlat && useFlat);
                ToggleClass(FlatButtonBordersClass,
                    resources?["UseFlatButtonBorders"] is bool flatBorders && flatBorders);
            }
            catch
            {
                // Presentation preferences must never prevent a dialog from opening.
            }
        }

        /// <summary>Adds the giblex.com dot field behind every Giblex-themed window.</summary>
        private void ApplyGiblexDotBackdrop()
        {
            if (_dotBackdropApplied || Classes.Contains("native-dot-grid") || Content is null || Application.Current is not App app)
                return;

            var runtimeTheme = app.Services?.GetService(typeof(IRuntimeThemeService)) as IRuntimeThemeService;
            if (!string.Equals(runtimeTheme?.CurrentThemeId, "GiblexGlassNavy", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                // Match giblex.com: neutral 1px noise at 8px, over a 1px grid at 40px.
                var grid = new DrawingBrush
                {
                    Drawing = new GeometryDrawing
                    {
                        Brush = null,
                        Pen = new Pen(new SolidColorBrush(Color.Parse("#08FFFFFF")), 1),
                        Geometry = new GeometryGroup
                        {
                            Children =
                            {
                                new LineGeometry(new Point(0.5, 0), new Point(0.5, 40)),
                                new LineGeometry(new Point(0, 0.5), new Point(40, 0.5))
                            }
                        }
                    },
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.None,
                    SourceRect = new RelativeRect(0, 0, 40, 40, RelativeUnit.Absolute),
                    DestinationRect = new RelativeRect(0, 0, 40, 40, RelativeUnit.Absolute)
                };
                var dots = new DrawingBrush
                {
                    Drawing = new GeometryDrawing
                    {
                        // CSS can retain a 1px dot at 4% opacity; Avalonia loses that
                        // subpixel mark after DPI composition. This is the perceptual
                        // desktop equivalent while preserving the website's 8px rhythm.
                        Brush = new SolidColorBrush(Color.Parse("#24FFFFFF")),
                        Geometry = new EllipseGeometry(new Rect(0, 0, 1.5, 1.5))
                    },
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.None,
                    SourceRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
                    DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
                    Opacity = 1.0
                };

                var originalContent = Content;
                Content = null;
                var layer = new Grid { IsHitTestVisible = true };
                layer.Children.Add(new Border
                {
                    Background = grid,
                    IsHitTestVisible = false
                });
                layer.Children.Add(new Border
                {
                    Background = dots,
                    IsHitTestVisible = false,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                });
                if (originalContent is Control control)
                    layer.Children.Add(control);
                else
                    layer.Children.Add(new ContentPresenter { Content = originalContent });

                Content = layer;
                _dotBackdropApplied = true;
            }
            catch
            {
                // A decorative backdrop must never stop a security dialog from opening.
            }
        }

        /// <summary>
        /// Stamps the live accent into this window's own Resources.
        ///
        /// UserAccentBrush is intentionally defined in no theme file — SetAccentColor is
        /// its only source — but ThemeManagerService only stamps windows that were already
        /// open when it ran. A window created later was left resolving the key through
        /// Application.Resources, which did not reliably reach controls inside a themed
        /// window: the brush came back null, and a null brush renders as nothing. That is
        /// what made a checked checkbox show its tick with no box behind it.
        ///
        /// Window.Resources sits above the app-level theme styles in the lookup, so
        /// stamping here resolves it for every window without reintroducing a theme-level
        /// definition that would shadow the user's chosen accent.
        /// </summary>
        private void ApplyAccentResources()
        {
            try
            {
                if (ThemeManagerService.CurrentAccent is not { } accent) return;

                Resources["UserAccentBrush"] = accent.Brush;
                Resources["UserAccentColor"] = accent.Color;
            }
            catch
            {
                // Never let accent stamping stop a window from opening.
            }
        }

        private void ToggleClass(string className, bool enabled)
        {
            if (enabled)
            {
                if (!Classes.Contains(className))
                {
                    Classes.Add(className);
                }
            }
            else
            {
                Classes.Remove(className);
            }
        }

        private void RegisterAndApplyTheme()
        {
            if (Application.Current is App app && app.Services != null)
            {
                var runtimeThemeService = app.Services.GetService(typeof(IRuntimeThemeService)) as RuntimeThemeService;
                if (runtimeThemeService != null)
                {

                    runtimeThemeService.RegisterWindow(this);

                    runtimeThemeService.ApplyToWindow(this);
                }
            }
        }

        private void UnregisterFromThemeService()
        {
            if (Application.Current is App app && app.Services != null)
            {
                var runtimeThemeService = app.Services.GetService(typeof(IRuntimeThemeService)) as RuntimeThemeService;
                runtimeThemeService?.UnregisterWindow(this);
            }
        }
    }
}

