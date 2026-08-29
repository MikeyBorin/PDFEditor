using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

public class ContentOverlayService
{
    public byte[] AddWatermark(byte[] pdfBytes, string text, string fontName = "Arial",
                                double fontSize = 72, string colorHex = "#FF0000",
                                double opacity = 0.3, double angleDegrees = -30)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var color = ParseColor(colorHex, opacity);
        var font = new XFont(fontName, fontSize, XFontStyle.Bold);
        var brush = new XSolidBrush(color);

        foreach (var page in doc.Pages)
        {
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            g.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
            g.RotateTransform(angleDegrees);
            g.DrawString(text, font, brush, new XPoint(0, 0), XStringFormats.Center);
        }
        return Save(doc);
    }

    public class HeaderFooterOptions
    {
        public string? HeaderLeft, HeaderCenter, HeaderRight;
        public string? FooterLeft, FooterCenter, FooterRight;
        public string FontName = "Arial";
        public double FontSize = 10;
        public string ColorHex = "#000000";
        public double MarginPoints = 24;
    }

    /// <summary>Placeholders: {page}, {total}, {date}, {filename}.</summary>
    public byte[] AddHeadersFooters(byte[] pdfBytes, HeaderFooterOptions o, string? filename = null)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var color = ParseColor(o.ColorHex, 1.0);
        var brush = new XSolidBrush(color);
        var font = new XFont(o.FontName, o.FontSize, XFontStyle.Regular);
        var total = doc.PageCount;
        var date = DateTime.Now.ToString("yyyy-MM-dd");

        for (int i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var w = page.Width.Point; var h = page.Height.Point;

            string Resolve(string? s) =>
                (s ?? "").Replace("{page}", (i + 1).ToString()).Replace("{total}", total.ToString())
                         .Replace("{date}", date).Replace("{filename}", filename ?? "");

            DrawText(g, Resolve(o.HeaderLeft), font, brush, new XRect(o.MarginPoints, o.MarginPoints, w - 2 * o.MarginPoints, 20), XStringFormats.TopLeft);
            DrawText(g, Resolve(o.HeaderCenter), font, brush, new XRect(o.MarginPoints, o.MarginPoints, w - 2 * o.MarginPoints, 20), XStringFormats.TopCenter);
            DrawText(g, Resolve(o.HeaderRight), font, brush, new XRect(o.MarginPoints, o.MarginPoints, w - 2 * o.MarginPoints, 20), XStringFormats.TopRight);

            DrawText(g, Resolve(o.FooterLeft), font, brush, new XRect(o.MarginPoints, h - o.MarginPoints - 20, w - 2 * o.MarginPoints, 20), XStringFormats.BottomLeft);
            DrawText(g, Resolve(o.FooterCenter), font, brush, new XRect(o.MarginPoints, h - o.MarginPoints - 20, w - 2 * o.MarginPoints, 20), XStringFormats.BottomCenter);
            DrawText(g, Resolve(o.FooterRight), font, brush, new XRect(o.MarginPoints, h - o.MarginPoints - 20, w - 2 * o.MarginPoints, 20), XStringFormats.BottomRight);
        }
        return Save(doc);
    }

    /// <summary>Bates numbering: fixed-width sequential numbering in the footer.</summary>
    public byte[] AddBates(byte[] pdfBytes, string prefix = "", int startNumber = 1, int digits = 6,
                           string colorHex = "#000000", double fontSize = 10, bool bottomRight = true)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var color = ParseColor(colorHex, 1.0);
        var brush = new XSolidBrush(color);
        var font = new XFont("Arial", fontSize, XFontStyle.Regular);
        for (int i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var w = page.Width.Point; var h = page.Height.Point;
            var num = (startNumber + i).ToString(new string('0', digits));
            var text = prefix + num;
            var fmt = bottomRight ? XStringFormats.BottomRight : XStringFormats.BottomLeft;
            g.DrawString(text, font, brush, new XRect(24, h - 40, w - 48, 20), fmt);
        }
        return Save(doc);
    }

    public byte[] InsertImage(byte[] pdfBytes, int pageIndex, string imagePath,
                              double xNorm, double yNorm, double widthNorm, double heightNorm)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        if (pageIndex < 0 || pageIndex >= doc.PageCount) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var page = doc.Pages[pageIndex];
        using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        using var img = XImage.FromFile(imagePath);
        g.DrawImage(img, xNorm * page.Width.Point, yNorm * page.Height.Point,
                    widthNorm * page.Width.Point, heightNorm * page.Height.Point);
        return Save(doc);
    }

    private static void DrawText(XGraphics g, string text, XFont font, XBrush brush, XRect rect, XStringFormat fmt)
    {
        if (!string.IsNullOrEmpty(text)) g.DrawString(text, font, brush, rect, fmt);
    }

    private static XColor ParseColor(string hex, double opacity)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
        var alpha = (byte)Math.Clamp((int)(opacity * 255), 0, 255);
        return XColor.FromArgb(alpha, c.R, c.G, c.B);
    }

    private static byte[] Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }
}
