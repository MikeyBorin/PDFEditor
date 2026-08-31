using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

/// <summary>
/// Applied-watermark record. Persisted into the PDF's Info dictionary under a
/// custom key (/ArtiMaxWatermarks) as JSON so the list survives Save + reopen —
/// enabling per-item Delete even after the document was closed and rebuilt.
/// </summary>
public record WatermarkRecord(
    string Id,
    string Text,
    string FontName,
    double FontSize,
    string ColorHex,
    double Opacity,
    double Angle,
    DateTime AppliedUtc);

public class WatermarkService
{
    private const string MetaKey = "/ArtiMaxWatermarks";

    /// <summary>Applies a watermark AND records it into the document's metadata
    /// so the Watermark manager can list/delete it later. Returns the new bytes.</summary>
    public byte[] Apply(byte[] pdfBytes, WatermarkRecord rec)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        DrawWatermark(doc, rec.Text, rec.FontName, rec.FontSize, rec.ColorHex, rec.Opacity, rec.Angle);
        var list = ReadRecords(doc);
        list.Add(rec);
        WriteRecords(doc, list);
        return Save(doc);
    }

    // Removal is intentionally NOT provided as a byte-rewriting operation. The only
    // supported delete path is a clean undo-restore in the caller (MainViewModel), which
    // is available when the "Watermark added" entry is still at the top of the undo
    // stack. Once a watermark has been flattened past the undo horizon (other edits done
    // since, or file saved and reopened), the only cover-up would be overpainting with
    // opaque white — which visibly damages any content behind the watermark and reads as
    // a document-integrity anti-pattern (a legitimate use case would use PDF layers).
    // Given this app targets casual authoring rather than forensics or DRM, the safer
    // stance is "no destructive delete" and let the user re-run their pipeline from a
    // clean source PDF if they need to change a baked-in watermark.

    public IReadOnlyList<WatermarkRecord> List(byte[] pdfBytes)
    {
        try
        {
            using var input = new MemoryStream(pdfBytes);
            var doc = PdfReader.Open(input, PdfDocumentOpenMode.InformationOnly);
            return ReadRecords(doc);
        }
        catch { return Array.Empty<WatermarkRecord>(); }
    }

    // -------- internals --------

    private static void DrawWatermark(PdfDocument doc, string text, string fontName,
                                      double fontSize, string colorHex, double opacity, double angleDegrees)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)!;
        var alpha = (byte)Math.Clamp((int)(opacity * 255), 0, 255);
        var xColor = XColor.FromArgb(alpha, c.R, c.G, c.B);
        var font = new XFont(fontName, fontSize, XFontStyle.Bold);
        var brush = new XSolidBrush(xColor);
        foreach (var page in doc.Pages)
        {
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            g.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
            g.RotateTransform(angleDegrees);
            g.DrawString(text, font, brush, new XPoint(0, 0), XStringFormats.Center);
        }
    }

    private static List<WatermarkRecord> ReadRecords(PdfDocument doc)
    {
        try
        {
            if (doc.Info.Elements.TryGetValue(MetaKey, out var item))
            {
                string? json = item switch
                {
                    PdfString s => s.Value,
                    PdfStringObject so => so.Value,
                    _ => item?.ToString()
                };
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var list = JsonSerializer.Deserialize<List<WatermarkRecord>>(json);
                    if (list != null) return list;
                }
            }
        }
        catch { }
        return new List<WatermarkRecord>();
    }

    private static void WriteRecords(PdfDocument doc, List<WatermarkRecord> list)
    {
        if (list.Count == 0)
        {
            if (doc.Info.Elements.ContainsKey(MetaKey))
                doc.Info.Elements.Remove(MetaKey);
            return;
        }
        var json = JsonSerializer.Serialize(list);
        doc.Info.Elements[MetaKey] = new PdfString(json, PdfStringEncoding.Unicode);
    }

    private static byte[] Save(PdfDocument doc)
    {
        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }
}
