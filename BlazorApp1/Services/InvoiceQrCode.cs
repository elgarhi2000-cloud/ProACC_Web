using System.Globalization;
using System.Text;
using QRCoder;

namespace BlazorApp1.Services;

public static class InvoiceQrCode
{
    // Five invoice data fields, UTF-8 TLV encoded and then Base64 encoded.
    // This is a data QR, not a signed or cleared electronic invoice.
    public static string Encode(string sellerName, string sellerVat, DateTimeOffset issuedAt,
        decimal totalWithVat, decimal vatTotal)
    {
        if (string.IsNullOrWhiteSpace(sellerName) || string.IsNullOrWhiteSpace(sellerVat))
            throw new ArgumentException("اسم البائع والرقم الضريبي مطلوبان لإنشاء رمز QR.");
        if (totalWithVat < 0 || vatTotal < 0 || vatTotal > totalWithVat)
            throw new ArgumentException("مبالغ رمز QR غير صحيحة.");

        string[] values = [sellerName.Trim(), sellerVat.Trim(),
            issuedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            totalWithVat.ToString("F2", CultureInfo.InvariantCulture),
            vatTotal.ToString("F2", CultureInfo.InvariantCulture)];
        using var stream = new MemoryStream();
        for (var index = 0; index < values.Length; index++)
        {
            var bytes = Encoding.UTF8.GetBytes(values[index]);
            if (bytes.Length > byte.MaxValue)
                throw new ArgumentException("بيانات البائع أطول من الحد المسموح به في رمز QR (255 بايت لكل حقل).");
            stream.WriteByte((byte)(index + 1));
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        return Convert.ToBase64String(stream.ToArray());
    }

    public static string CreateImage(string payload)
    {
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var renderer = new PngByteQRCode(data);
        return "data:image/png;base64," + Convert.ToBase64String(renderer.GetGraphic(8));
    }
}
