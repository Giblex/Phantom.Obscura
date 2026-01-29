using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Visuals;
using SkiaSharp;

namespace PhantomVault.UI.Controls;

/// <summary>
/// Glossy, refractive gel highlight bar that magnifies the content behind it using a Skia runtime shader.
/// </summary>
public partial class GelMagnifyHighlightBar : UserControl
{
    public static readonly StyledProperty<Visual?> SourceElementProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, Visual?>(nameof(SourceElement));

    public static readonly StyledProperty<double> RefractionStrengthProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, double>(nameof(RefractionStrength), 0.018);

    public static readonly StyledProperty<double> LensRadiusFactorProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, double>(nameof(LensRadiusFactor), 0.96);

    public static readonly StyledProperty<double> IndexOfRefractionProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, double>(nameof(IndexOfRefraction), 1.35);

    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, Color>(nameof(TintColor), Color.Parse("#e2f0f8ff"));

    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, double>(nameof(TintOpacity), 0.32);

    public static readonly StyledProperty<double> MaterialOpacityProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, double>(nameof(MaterialOpacity), 0.56);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<GelMagnifyHighlightBar, string>(nameof(Label), "Hover");

    static GelMagnifyHighlightBar()
    {
        SourceElementProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        RefractionStrengthProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        LensRadiusFactorProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        IndexOfRefractionProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        TintColorProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        TintOpacityProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        MaterialOpacityProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        LabelProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
        CornerRadiusProperty.OverrideDefaultValue<GelMagnifyHighlightBar>(new CornerRadius(18));
        CornerRadiusProperty.Changed.AddClassHandler<GelMagnifyHighlightBar>((x, _) => x.InvalidateVisual());
    }

    public Visual? SourceElement
    {
        get => GetValue(SourceElementProperty);
        set => SetValue(SourceElementProperty, value);
    }

    public double RefractionStrength
    {
        get => GetValue(RefractionStrengthProperty);
        set => SetValue(RefractionStrengthProperty, value);
    }

    public double LensRadiusFactor
    {
        get => GetValue(LensRadiusFactorProperty);
        set => SetValue(LensRadiusFactorProperty, value);
    }

    public double IndexOfRefraction
    {
        get => GetValue(IndexOfRefractionProperty);
        set => SetValue(IndexOfRefractionProperty, value);
    }

    public Color TintColor
    {
        get => GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public double TintOpacity
    {
        get => GetValue(TintOpacityProperty);
        set => SetValue(TintOpacityProperty, value);
    }

    public double MaterialOpacity
    {
        get => GetValue(MaterialOpacityProperty);
        set => SetValue(MaterialOpacityProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private DispatcherTimer? _timer;
    private double _velY;
    private double _offsetY;
    private double _nx = 0.5;
    private double _ny = 0.5;
    private SKImage? _noise;

    private const double SpringStrength = 0.12;
    private const double Damping = 0.82;
    private const double MaxBob = 6.0;
    private const double MaxVel = 3.5;
    private const double ImpulseEnter = -2.5;
    private const double ImpulseExit = 1.8;

    private const string SkSl = @"
uniform shader uTex;
uniform float2 uSize;
uniform float uTime;
uniform float uStrength;
uniform float uLensRadius;
uniform float uIOR;
uniform float2 uParallax;
half4 main(float2 fragCoord) {
  float2 uv = fragCoord / uSize;
  float2 center = float2(0.5,0.5) + uParallax * 0.01;
  float2 d = uv - center;
  float r = length(d);
  float rN = r / (uLensRadius * 0.5);
  float wob = 0.003 * sin( (uv.x+uv.y)*24.0 + uTime*2.0 );
  float fall = smoothstep(1.0, 0.0, rN);
  float bend = uStrength * fall * (uIOR - 1.0);
  float2 dir = normalize(d + 1e-5);
  float2 ofs = -dir * bend + wob * dir.yx;
  half4 col = uTex.eval((uv + ofs) * uSize);
  float ring = smoothstep(0.98, 0.8, fall);
  col.rgb += ring * 0.06;
  return col;
}";

    public GelMagnifyHighlightBar()
    {
        InitializeComponent();
        IsHitTestVisible = true;

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerMoved += OnPointerMoved;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTimerTick;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_timer is not null)
        {
            _timer.Tick -= OnTimerTick;
            _timer.Stop();
            _timer = null;
        }

        _noise?.Dispose();
        _noise = null;

        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var width = Math.Max(1, (int)Bounds.Width);
        var height = Math.Max(1, (int)Bounds.Height);
        var rect = new Rect(Bounds.Size);
        var clip = new RoundedRect(rect, CornerRadius);

        using (context.PushClip(clip))
        using (context.PushTransform(Matrix.CreateTranslation(0, _offsetY)))
        {
            using var rtb = new RenderTargetBitmap(new PixelSize(width, height));
            if (SourceElement is { } src)
            {
                rtb.Render(src);
            }

            using var stream = new MemoryStream();
            rtb.Save(stream);
            stream.Position = 0;
            using var baseImage = SKImage.FromEncodedData(stream);

            using var effect = SKRuntimeEffect.CreateShader(SkSl, out var errorText);
            if (effect is null)
            {
                throw new InvalidOperationException("SkSL compile error: " + errorText);
            }

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var parallaxX = (float)((_nx - 0.5) * 2);
            var parallaxY = (float)((0.5 - _ny) * 2);
            var timeSeconds = (float)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
            var lensRadius = (float)(Math.Min(width, height) * LensRadiusFactor);

            var uniforms = new SKRuntimeEffectUniforms(effect);
            uniforms["uSize"] = new[] { (float)width, (float)height };
            uniforms["uTime"] = timeSeconds;
            uniforms["uStrength"] = (float)RefractionStrength;
            uniforms["uLensRadius"] = lensRadius;
            uniforms["uIOR"] = (float)IndexOfRefraction;
            uniforms["uParallax"] = new[] { parallaxX, parallaxY };

            var children = new SKRuntimeEffectChildren(effect);
            using var textureShader = baseImage.ToShader();
            children["uTex"] = textureShader;

            using var shader = effect.ToShader(uniforms, children);
            using var paint = new SKPaint { Shader = shader };

            var skRect = new SKRect(0, 0, width, height);
            var radius = (float)CornerRadius.TopLeft;
            var roundRect = new SKRoundRect(skRect, radius, radius);

            canvas.Save();
            canvas.ClipRoundRect(roundRect, SKClipOperation.Intersect, true);
            canvas.DrawRect(skRect, paint);
            canvas.Restore();

            var tintAlpha = (byte)Math.Clamp(TintOpacity * MaterialOpacity * 255, 0, 255);
            var tintColor = new SKColor(TintColor.R, TintColor.G, TintColor.B, tintAlpha);
            using var tintPaint = new SKPaint { Color = tintColor, BlendMode = SKBlendMode.SrcOver };
            canvas.DrawRect(skRect, tintPaint);

            DrawEdgeBloom(canvas, width, height);
            DrawInnerShadow(canvas, width, height);
            DrawNoise(canvas, width, height);
            DrawSheen(canvas, width, height);
            DrawCaustic(canvas, width, height);
            DrawLabel(canvas, width, height);

            using var finalImage = surface.Snapshot();
            using var encoded = finalImage.Encode(SKEncodedImageFormat.Png, 100);
            using var finalStream = new MemoryStream();
            encoded.SaveTo(finalStream);
            finalStream.Position = 0;
            using var bitmap = new Bitmap(finalStream);
            var destRect = new Rect(0, 0, width, height);
            var sourceRect = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            context.DrawImage(bitmap, sourceRect, destRect);
        }
    }

    private void DrawOverlaysFallback(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)), null, rect.Inflate(-1));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)), 2), rect.Inflate(-1));
    }

    private void DrawEdgeBloom(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, height),
                new[] { new SKColor(255, 255, 255, 64), new SKColor(255, 255, 255, 16), new SKColor(255, 255, 255, 0) },
                new float[] { 0f, 0.4f, 1f },
                SKShaderTileMode.Clamp)
        };
        var radius = (float)CornerRadius.TopLeft;
        canvas.DrawRoundRect(new SKRect(1, 1, width - 1, height - 1), radius, radius, paint);
    }

    private void DrawInnerShadow(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(width / 2f, height / 2f),
                Math.Min(width, height) * 0.45f,
                new[] { new SKColor(0, 0, 0, 16), new SKColor(0, 0, 0, 0) },
                new float[] { 0.75f, 1f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private void DrawNoise(SKCanvas canvas, int width, int height)
    {
        _noise ??= CreateNoiseTile();
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(0.06 * 255)),
            Shader = _noise.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                SKMatrix.CreateScale((float)width / 128f, (float)height / 128f))
        };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private void DrawSheen(SKCanvas canvas, int width, int height)
    {
        var cx = (float)(width * (0.25 + (_nx - 0.5) * 0.05));
        var cy = (float)(height * (0.02 + (0.5 - _ny) * 0.03));
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                Math.Max(width, height) * 0.7f,
                new[]
                {
                    new SKColor(255, 255, 255, 180),
                    new SKColor(255, 255, 255, 32),
                    new SKColor(255, 255, 255, 0)
                },
                new float[] { 0f, 0.45f, 1f },
                SKShaderTileMode.Clamp)
        };
#pragma warning disable CS0618
        paint.FilterQuality = SKFilterQuality.High;
#pragma warning restore CS0618
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private void DrawCaustic(SKCanvas canvas, int width, int height)
    {
        var t = (float)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        var x0 = -0.2f * width + (float)Math.Sin(t * 1.2) * 10f;
        var x1 = 1.2f * width + (float)Math.Sin(t * 1.2 + 1.57) * 10f;
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(x0, 0), new SKPoint(x1, height),
                new[]
                {
                    new SKColor(255, 255, 255, 0),
                    new SKColor(255, 255, 255, 180),
                    new SKColor(159, 216, 255, 50),
                    new SKColor(255, 255, 255, 0)
                },
                new float[] { 0f, 0.4f, 0.55f, 0.9f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    private void DrawLabel(SKCanvas canvas, int width, int height)
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI Semibold", SKFontStyle.Normal);
        using var font = new SKFont(typeface, 14);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 240)
        };
        var textWidth = font.MeasureText(Label, paint);
        canvas.DrawText(Label, (width - textWidth) / 2f, height / 2f + 5, SKTextAlign.Left, font, paint);
    }

    private void StepSpring()
    {
        var displacement = -_offsetY;
        var accel = displacement * SpringStrength;
        _velY = (_velY + accel) * Damping;
        _velY = Math.Clamp(_velY, -MaxVel, MaxVel);
        _offsetY = Math.Clamp(_offsetY + _velY, -MaxBob, MaxBob);
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e) => _velY += ImpulseEnter;

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _velY += ImpulseExit;
        _nx = 0.5;
        _ny = 0.5;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var point = e.GetPosition(this);
        _nx = Math.Clamp(point.X / Bounds.Width, 0, 1);
        _ny = Math.Clamp(point.Y / Bounds.Height, 0, 1);
        _velY += (0.5 - _ny) * 0.05;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        StepSpring();
        InvalidateVisual();
    }

    private static SKImage CreateNoiseTile()
    {
        var random = new Random(1234);
        using var bitmap = new SKBitmap(new SKImageInfo(128, 128, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < 128; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                var noise = (byte)random.Next(0, 255);
                bitmap.SetPixel(x, y, new SKColor(noise, noise, noise, 255));
            }
        }

        return SKImage.FromBitmap(bitmap);
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
