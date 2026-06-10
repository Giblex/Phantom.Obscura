using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace PhantomVault.UI.Services
{
    // Renders a printable recovery-kit PDF using SkiaSharp (already referenced for QR/imaging),
    // so no extra PDF dependency is introduced. US-Letter page, monospaced codes, plain text only
    // — the codes themselves are the sensitive material, so callers must guard the destination path.
    public static class RecoveryPdfWriter
    {
        private const float PageWidth = 612f;   // 8.5in * 72
        private const float PageHeight = 792f;  // 11in * 72
        private const float Margin = 56f;

        public static void Write(string path, string vaultName, IReadOnlyList<string> codes)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path required", nameof(path));
            codes ??= Array.Empty<string>();

            using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var doc = SKDocument.CreatePdf(stream);
            var canvas = doc.BeginPage(PageWidth, PageHeight);

            using var title = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 20f);
            using var heading = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 12f);
            using var body = new SKFont(SKTypeface.FromFamilyName("Arial"), 11f);
            using var mono = new SKFont(SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default, 14f);
            using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var muted = new SKPaint { Color = new SKColor(90, 90, 90), IsAntialias = true };
            using var rule = new SKPaint { Color = new SKColor(210, 210, 210), IsAntialias = true, StrokeWidth = 1f };

            float y = Margin;

            canvas.DrawText("Phantom Obscura — Recovery Kit", Margin, y, title, ink);
            y += 26f;

            var safeName = string.IsNullOrWhiteSpace(vaultName) ? "Vault" : vaultName.Trim();
            canvas.DrawText($"Vault: {safeName}", Margin, y, body, ink);
            y += 16f;
            canvas.DrawText($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", Margin, y, body, muted);
            y += 24f;

            canvas.DrawLine(Margin, y, PageWidth - Margin, y, rule);
            y += 24f;

            foreach (var line in WrapText(
                "If your USB is ever lost or damaged, these recovery codes are the only way to rebuild " +
                "your keyfile and regain access. Store this sheet somewhere safe and separate from the USB. " +
                "Anyone who obtains a code can begin recovery, so treat it like cash.",
                body, ink, PageWidth - (Margin * 2)))
            {
                canvas.DrawText(line, Margin, y, body, ink);
                y += 16f;
            }
            y += 16f;

            canvas.DrawText("Recovery codes (any one can open the recovery file):", Margin, y, heading, ink);
            y += 24f;

            for (int i = 0; i < codes.Count; i++)
            {
                if (y > PageHeight - Margin)
                {
                    doc.EndPage();
                    canvas = doc.BeginPage(PageWidth, PageHeight);
                    y = Margin;
                }
                canvas.DrawText($"{i + 1,2}.  {codes[i]}", Margin, y, mono, ink);
                y += 22f;
            }

            doc.EndPage();
            doc.Close();
        }

        private static IEnumerable<string> WrapText(string text, SKFont font, SKPaint paint, float maxWidth)
        {
            var words = text.Split(' ');
            var line = string.Empty;
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (font.MeasureText(candidate, paint) > maxWidth && line.Length > 0)
                {
                    yield return line;
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            if (line.Length > 0) yield return line;
        }
    }
}
