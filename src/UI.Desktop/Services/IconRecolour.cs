using System;
using System.IO;
using System.Linq;
using Avalonia.Media;
using SkiaSharp;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Saves a recoloured copy of a single-colour icon. Every non-transparent pixel takes the
    /// chosen colour and keeps its own alpha, so anti-aliased edges survive. Replaces the ten
    /// pre-coloured copies each category glyph used to ship with.
    /// </summary>
    public static class IconRecolour
    {
        private const int MaxSvgSize = 256;

        /// <param name="baseName">Name for the output file (defaults to the source file name).</param>
        /// <returns>The path of the recoloured PNG (reused if it already exists).</returns>
        public static string SaveRecoloured(string sourcePath, Color color, string outputDirectory, string? baseName = null)
        {
            Directory.CreateDirectory(outputDirectory);

            var hex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
            var name = Sanitize(string.IsNullOrWhiteSpace(baseName) ? Path.GetFileNameWithoutExtension(sourcePath) : baseName);
            var target = Path.Combine(outputDirectory, $"{name}_{hex}.png");
            if (File.Exists(target)) return target;

            using var source = Load(sourcePath) ?? throw new InvalidDataException("The icon image could not be read.");
            using var sourceImage = SKImage.FromBitmap(source);
            using var output = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(output))
            using (var paint = new SKPaint
                   {
                       IsAntialias = true,
                       // SrcIn keeps the icon's alpha and replaces its colour.
                       ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(color.R, color.G, color.B, 255), SKBlendMode.SrcIn)
                   })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawImage(sourceImage, 0, 0, paint);
                canvas.Flush();
            }

            using var image = SKImage.FromBitmap(output);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(target))
            {
                data.SaveTo(stream);
            }

            return target;
        }

        private static SKBitmap? Load(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
                return SKBitmap.Decode(path);

            using var svg = new Svg.Skia.SKSvg();
            var picture = svg.Load(path);
            if (picture == null) return null;

            var bounds = picture.CullRect;
            float scale = Math.Min(MaxSvgSize / Math.Max(1f, bounds.Width), MaxSvgSize / Math.Max(1f, bounds.Height));
            int width = Math.Max(1, (int)(bounds.Width * scale));
            int height = Math.Max(1, (int)(bounds.Height * scale));

            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
            return bitmap;
        }

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return cleaned.Length == 0 ? "icon" : cleaned;
        }
    }
}
