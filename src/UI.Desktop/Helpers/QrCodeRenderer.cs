using System;
using System.IO;
using Avalonia.Media.Imaging;
using QRCoder;

namespace PhantomVault.UI.Helpers
{

    public static class QrCodeRenderer
    {
        private const int MaxPayloadLength = 2900;

        public static Bitmap? Render(string? payload, int pixelsPerModule = 6)
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxPayloadLength)
                return null;

            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);

                var png = new PngByteQRCode(data).GetGraphic(Math.Clamp(pixelsPerModule, 2, 20));
                using var stream = new MemoryStream(png);
                return new Bitmap(stream);
            }
            catch
            {

                return null;
            }
        }

        public static QRCodeGenerator.ECCLevel ParseEccLevel(string? value) => value?.Trim().ToUpperInvariant() switch
        {
            "L" => QRCodeGenerator.ECCLevel.L,
            "M" => QRCodeGenerator.ECCLevel.M,
            "H" => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.Q
        };
    }
}
