using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform;
using PhantomVault.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Reusable SVG icon control that renders only embedded deterministic assets
/// or shared built-in geometries. Network-backed icon fetching is prohibited.
/// </summary>
public partial class SvgIcon : UserControl
{
    private Avalonia.Controls.Shapes.Path? _iconPath;
    private Canvas? _iconCanvas;

    private static readonly Dictionary<IconPreset, string> PresetGeometryMap = new()
    {
        [IconPreset.Search] = "M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z",
        [IconPreset.Lock] = "M19 11H5a2 2 0 00-2 2v7a2 2 0 002 2h14a2 2 0 002-2v-7a2 2 0 00-2-2zm-2-2V7a5 5 0 00-10 0v2h10zM9 7a3 3 0 016 0v2H9V7z",
        [IconPreset.Unlock] = "M7 11V7a5 5 0 019.9-1M5 11h14a2 2 0 012 2v7a2 2 0 01-2 2H5a2 2 0 01-2-2v-7a2 2 0 012-2z",
        [IconPreset.Eye] = "M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8zM12 15a3 3 0 100-6 3 3 0 000 6z",
        [IconPreset.EyeOff] = "M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24M1 1l22 22",
        [IconPreset.Copy] = "M20 9h-9a2 2 0 00-2 2v9a2 2 0 002 2h9a2 2 0 002-2v-9a2 2 0 00-2-2zM5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1",
        [IconPreset.Check] = "M20 6L9 17l-5-5",
        [IconPreset.X] = "M18 6L6 18M6 6l12 12",
        [IconPreset.AlertCircle] = "M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10zm0-14v4m0 4v.01",
        [IconPreset.AlertTriangle] = "M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0zM12 9v4m0 4v.01",
        [IconPreset.Info] = "M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10zm0-14v.01M12 11v6",
        [IconPreset.Plus] = "M12 5v14M5 12h14",
        [IconPreset.Minus] = "M5 12h14",
        [IconPreset.Edit] = "M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z",
        [IconPreset.Trash] = "M3 6h18M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2m3 0v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6h14zM10 11v6M14 11v6",
        [IconPreset.Download] = "M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3",
        [IconPreset.Upload] = "M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M17 8l-5-5-5 5M12 3v12",
        [IconPreset.Settings] = "M12 15a3 3 0 100-6 3 3 0 000 6z M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-2 2 2 2 0 01-2-2v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83 0 2 2 0 010-2.83l.06-.06a1.65 1.65 0 00.33-1.82 1.65 1.65 0 00-1.51-1H3a2 2 0 01-2-2 2 2 0 012-2h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 010-2.83 2 2 0 012.83 0l.06.06a1.65 1.65 0 001.82.33H9a1.65 1.65 0 001-1.51V3a2 2 0 012-2 2 2 0 012 2v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 0 2 2 0 010 2.83l-.06.06a1.65 1.65 0 00-.33 1.82V9a1.65 1.65 0 001.51 1H21a2 2 0 012 2 2 2 0 01-2 2h-.09a1.65 1.65 0 00-1.51 1z",
        [IconPreset.User] = "M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2M12 11a4 4 0 100-8 4 4 0 000 8z",
        [IconPreset.Key] = "M21 2l-2 2m-7.61 7.61a5.5 5.5 0 11-7.778 7.778 5.5 5.5 0 017.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4",
        [IconPreset.Shield] = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z",
        [IconPreset.Star] = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z",
        [IconPreset.Heart] = "M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z",
        [IconPreset.Folder] = "M22 19a2 2 0 01-2 2H4a2 2 0 01-2-2V5a2 2 0 012-2h5l2 3h9a2 2 0 012 2v11z",
        [IconPreset.File] = "M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8l-6-6zm4 18H6V4h7v5h5v11z",
        [IconPreset.Calendar] = "M19 4H5a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2V6a2 2 0 00-2-2zM16 2v4M8 2v4M3 10h18",
        [IconPreset.Clock] = "M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10zM12 6v6l4 2",
        [IconPreset.Database] = "M12 2c4.97 0 9 1.79 9 4v12c0 2.21-4.03 4-9 4s-9-1.79-9-4V6c0-2.21 4.03-4 9-4zM21 12c0 2.21-4.03 4-9 4s-9-1.79-9-4M21 6c0 2.21-4.03 4-9 4S3 8.21 3 6",
        [IconPreset.Usb] = "M12 22a4 4 0 004-4v-5h4l-8-8-8 8h4v5a4 4 0 004 4zM8 10V5h8v5",
        [IconPreset.ChevronLeft] = "M15 18l-6-6 6-6",
        [IconPreset.ChevronRight] = "M9 18l6-6-6-6",
        [IconPreset.ChevronDown] = "M6 9l6 6 6-6",
        [IconPreset.ChevronUp] = "M18 15l-6-6-6 6",
        [IconPreset.Menu] = "M3 12h18M3 6h18M3 18h18",
        [IconPreset.MoreVertical] = "M12 13a1 1 0 100-2 1 1 0 000 2zm0-5a1 1 0 100-2 1 1 0 000 2zm0 10a1 1 0 100-2 1 1 0 000 2z"
    };

    private static readonly Dictionary<string, IconPreset> IconNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["search"] = IconPreset.Search,
        ["lock"] = IconPreset.Lock,
        ["unlock"] = IconPreset.Unlock,
        ["eye"] = IconPreset.Eye,
        ["eye-off"] = IconPreset.EyeOff,
        ["copy"] = IconPreset.Copy,
        ["check"] = IconPreset.Check,
        ["x"] = IconPreset.X,
        ["alert-circle"] = IconPreset.AlertCircle,
        ["alert-triangle"] = IconPreset.AlertTriangle,
        ["info"] = IconPreset.Info,
        ["plus"] = IconPreset.Plus,
        ["minus"] = IconPreset.Minus,
        ["edit"] = IconPreset.Edit,
        ["trash"] = IconPreset.Trash,
        ["download"] = IconPreset.Download,
        ["upload"] = IconPreset.Upload,
        ["settings"] = IconPreset.Settings,
        ["user"] = IconPreset.User,
        ["key"] = IconPreset.Key,
        ["shield"] = IconPreset.Shield,
        ["star"] = IconPreset.Star,
        ["heart"] = IconPreset.Heart,
        ["folder"] = IconPreset.Folder,
        ["file"] = IconPreset.File,
        ["calendar"] = IconPreset.Calendar,
        ["clock"] = IconPreset.Clock,
        ["database"] = IconPreset.Database,
        ["usb"] = IconPreset.Usb,
        ["chevron-left"] = IconPreset.ChevronLeft,
        ["chevron-right"] = IconPreset.ChevronRight,
        ["chevron-down"] = IconPreset.ChevronDown,
        ["chevron-up"] = IconPreset.ChevronUp,
        ["menu"] = IconPreset.Menu,
        ["more-vertical"] = IconPreset.MoreVertical,
        ["activity"] = IconPreset.Clock
    };

    private static readonly Regex ViewBoxRegex = new(@"viewBox\s*=\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PathRegex = new(@"<path\b[^>]*\sd\s*=\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LineRegex = new(
        @"<line\s[^>]*?x1\s*=\s*""([^""]+)""\s[^>]*?y1\s*=\s*""([^""]+)""\s[^>]*?x2\s*=\s*""([^""]+)""\s[^>]*?y2\s*=\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static readonly StyledProperty<IconPreset?> PresetProperty =
        AvaloniaProperty.Register<SvgIcon, IconPreset?>(nameof(Preset));

    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<SvgIcon, string?>(nameof(IconName));

    public static readonly StyledProperty<int> IconSizeProperty =
        AvaloniaProperty.Register<SvgIcon, int>(nameof(IconSize), defaultValue: 24);

    public static readonly StyledProperty<IBrush?> IconColorProperty =
        AvaloniaProperty.Register<SvgIcon, IBrush?>(nameof(IconColor), defaultValue: Brushes.White);

    public IconPreset? Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public int IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IBrush? IconColor
    {
        get => GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public SvgIcon()
    {
        InitializeComponent();
        _iconPath = this.FindControl<Avalonia.Controls.Shapes.Path>("IconPath");
        _iconCanvas = this.FindControl<Canvas>("IconCanvas");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PresetProperty ||
            change.Property == IconNameProperty ||
            change.Property == IconSizeProperty ||
            change.Property == IconColorProperty)
        {
            _ = LoadIconAsync();
        }
    }

    private async Task LoadIconAsync()
    {
        if (_iconPath == null)
            return;

        try
        {
            Geometry? geometry = null;
            string? svgContent = null;

            if (Preset.HasValue)
            {
                geometry = TryBuildPresetGeometry(Preset.Value);
            }
            else if (!string.IsNullOrWhiteSpace(IconName))
            {
                if (TryResolveNamedGeometry(IconName, out var namedGeometry))
                {
                    geometry = namedGeometry;
                }
                else
                {
                    svgContent = await LoadEmbeddedSvgAsync(IconName);
                }
            }

            if (geometry != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_iconCanvas != null)
                    {
                        _iconCanvas.Width = 24;
                        _iconCanvas.Height = 24;
                    }

                    _iconPath.Data = geometry;
                    _iconPath.StrokeThickness = 1.5;
                });
                return;
            }

            if (!string.IsNullOrWhiteSpace(svgContent))
            {
                await ApplyLocalSvgAsync(svgContent);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => _iconPath.Data = null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgIcon] Failed to load icon: {ex.Message}");
        }
    }

    private static Geometry? TryBuildPresetGeometry(IconPreset preset)
    {
        return PresetGeometryMap.TryGetValue(preset, out var geometryData)
            ? Geometry.Parse(geometryData)
            : null;
    }

    private static bool TryResolveNamedGeometry(string iconName, out Geometry? geometry)
    {
        if (IconNameAliases.TryGetValue(iconName.Trim(), out var preset))
        {
            geometry = TryBuildPresetGeometry(preset);
            return geometry != null;
        }

        geometry = null;
        return false;
    }

    private static async Task<string?> LoadEmbeddedSvgAsync(string iconName)
    {
        var normalizedPath = NormalizeEmbeddedSvgPath(iconName);
        if (normalizedPath == null)
            return null;

        try
        {
            var uri = new Uri($"avares://PhantomVault.UI/{normalizedPath}");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgIcon] Failed to open embedded SVG '{iconName}': {ex.Message}");
            return null;
        }
    }

    private static string? NormalizeEmbeddedSvgPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var path = rawPath.Trim();

        const string assemblyUriPrefix = "avares://PhantomVault.UI/";
        if (path.StartsWith(assemblyUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[assemblyUriPrefix.Length..];
        }

        path = path.TrimStart('/', '\\').Replace('\\', '/');

        if (!path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return null;

        if (path.StartsWith("Assets/Visuals/App Icons/SVG/", StringComparison.OrdinalIgnoreCase))
            return path;

        if (path.StartsWith("Assets/SVG/Current/", StringComparison.OrdinalIgnoreCase))
            return "Assets/Visuals/App Icons/SVG/" + path["Assets/SVG/Current/".Length..];

        if (path.StartsWith("Assets/SVG/", StringComparison.OrdinalIgnoreCase))
            return "Assets/Visuals/App Icons/SVG/" + path["Assets/SVG/".Length..];

        if (path.StartsWith("Assets/Icons/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Assets/Visuals/", StringComparison.OrdinalIgnoreCase))
            return path;

        return "Assets/Visuals/App Icons/SVG/" + Path.GetFileName(path);
    }

    private async Task ApplyLocalSvgAsync(string svgContent)
    {
        if (_iconPath == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (_iconCanvas != null)
                {
                    _iconCanvas.Width = 24;
                    _iconCanvas.Height = 24;
                }

                var viewBoxMatch = ViewBoxRegex.Match(svgContent);
                if (viewBoxMatch.Success && _iconCanvas != null)
                {
                    var parts = viewBoxMatch.Groups[1].Value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 4 &&
                        double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var viewBoxWidth) &&
                        double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var viewBoxHeight))
                    {
                        _iconCanvas.Width = viewBoxWidth;
                        _iconCanvas.Height = viewBoxHeight;
                        _iconPath.StrokeThickness = 2.0 * (viewBoxWidth / 24.0);
                    }
                }

                var geometries = new List<Geometry>();

                foreach (Match match in PathRegex.Matches(svgContent))
                {
                    try
                    {
                        geometries.Add(Geometry.Parse(match.Groups[1].Value));
                    }
                    catch
                    {
                    }
                }

                foreach (Match match in LineRegex.Matches(svgContent))
                {
                    try
                    {
                        geometries.Add(Geometry.Parse($"M{match.Groups[1].Value} {match.Groups[2].Value}L{match.Groups[3].Value} {match.Groups[4].Value}"));
                    }
                    catch
                    {
                    }
                }

                if (geometries.Count == 0)
                {
                    _iconPath.Data = null;
                    return;
                }

                if (geometries.Count == 1)
                {
                    _iconPath.Data = geometries[0];
                    return;
                }

                var group = new GeometryGroup();
                foreach (var geometry in geometries)
                {
                    group.Children.Add(geometry);
                }

                _iconPath.Data = group;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SvgIcon] Failed to parse local SVG: {ex.Message}");
                _iconPath.Data = null;
            }
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
