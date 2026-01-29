using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform;
using PhantomVault.Core.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Svg.Skia;

namespace PhantomVault.UI.Desktop.Controls;

/// <summary>
/// Reusable SVG icon control that fetches icons from SVGAPI.
/// Replaces emoji icons with professional vector graphics.
/// </summary>
public partial class SvgIcon : UserControl
{
    private static SvgIconService? _iconService;
    private Avalonia.Controls.Shapes.Path? _iconPath;

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

        // Initialize service (singleton)
        _iconService ??= new SvgIconService("ZluQ8qGZzB");
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
        if (_iconService == null || _iconPath == null)
            return;

        try
        {
            string? svgContent = null;

            // Determine color
            var colorHex = IconColor != null ? GetColorHex(IconColor) : "currentColor";

            // Load from preset or custom name
            if (Preset.HasValue)
            {
                svgContent = await _iconService.GetPresetIconAsync(Preset.Value, IconSize, colorHex);
            }
            else if (!string.IsNullOrWhiteSpace(IconName))
            {
                if (IconName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var path = IconName.TrimStart('/');
                    var uri = new Uri($"avares://PhantomVault.UI/{path}");
                    using var stream = AssetLoader.Open(uri);
                    using var reader = new StreamReader(stream);
                    svgContent = await reader.ReadToEndAsync();
                }
                else
                {
                    svgContent = await _iconService.GetIconAsync(IconName, IconSize, colorHex);
                }
            }

            if (!string.IsNullOrWhiteSpace(svgContent))
            {
                // Parse SVG and extract path data
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        // Extract all path data from SVG (handles multiple paths, circles, lines, etc.)
                        var geometries = new List<Geometry>();
                        
                        // Extract all d="" attributes from path elements
                        int searchIndex = 0;
                        while (true)
                        {
                            var pathDataStart = svgContent.IndexOf("d=\"", searchIndex, StringComparison.Ordinal);
                            if (pathDataStart < 0) break;
                            
                            pathDataStart += 3;
                            var pathDataEnd = svgContent.IndexOf("\"", pathDataStart, StringComparison.Ordinal);
                            if (pathDataEnd > pathDataStart)
                            {
                                var pathData = svgContent.Substring(pathDataStart, pathDataEnd - pathDataStart);
                                try
                                {
                                    geometries.Add(Geometry.Parse(pathData));
                                }
                                catch { /* Skip invalid path data */ }
                            }
                            searchIndex = pathDataEnd + 1;
                        }
                        
                        // Combine all geometries
                        if (geometries.Count > 0)
                        {
                            if (geometries.Count == 1)
                            {
                                _iconPath.Data = geometries[0];
                            }
                            else
                            {
                                var group = new GeometryGroup();
                                foreach (var geom in geometries)
                                {
                                    group.Children.Add(geom);
                                }
                                _iconPath.Data = group;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SvgIcon] Failed to parse SVG: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgIcon] Failed to load icon: {ex.Message}");
        }
    }

    private string GetColorHex(IBrush brush)
    {
        if (brush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return "currentColor";
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
