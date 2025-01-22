namespace DashboardReportApp.Services
{
    using FastReport.Code.CodeDom.Compiler;
    using QRCoder.Core;
    using System;
    using System.Drawing;
    using System.IO;

    public class QRCodeService
    {
        public byte[] GenerateQRCode(string data)
        {
            using (var qrGenerator = new QRCodeGenerator())
            {
                var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
                using (var qrCode = new QRCode(qrCodeData))
                {
                    using (var bitmap = qrCode.GetGraphic(20))
                    {
                        using (var stream = new MemoryStream())
                        {
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            return stream.ToArray();
                        }
                    }
                }
            }
        }
    }

}
