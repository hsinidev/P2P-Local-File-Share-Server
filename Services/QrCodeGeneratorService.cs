using System;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace P2PLocalFileShareServer.Services
{
    public class QrCodeGeneratorService
    {
        public BitmapSource GenerateQrCode(string content, int pixelsPerModule = 15)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                content = "http://localhost:8080";
            }

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);

            // Dark color: Emerald #10B981 => R=16, G=185, B=129
            // Light color: Surface Card #182232 => R=24, G=34, B=50
            byte[] darkColor = new byte[] { 16, 185, 129 };
            byte[] lightColor = new byte[] { 24, 34, 50 };

            byte[] qrCodeBytes = qrCode.GetGraphic(pixelsPerModule, darkColor, lightColor);

            using var stream = new MemoryStream(qrCodeBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
    }
}
