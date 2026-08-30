using System.Collections.Generic;
using System.IO;
using System.Linq;
using PDFEditor.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

/// <summary>
/// Flattens WPF-side annotations onto the actual PDF pages using XGraphics.
/// Simple and robust; annotations become permanent page content.
/// </summary>
public class AnnotationService
{
    public byte[] Flatten(byte[] pdfBytes, IEnumerable<PdfAnnotation> annotations)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        var byPage = annotations.GroupBy(a => a.PageIndex);
        foreach (var group in byPage)
        {
            if (group.Key < 0 || group.Key >= doc.PageCount) continue;
            var page = doc.Pages[group.Key];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            double w = page.Width.Point;
            double h = page.Height.Point;

            foreach (var a in group)
            {
                var color = XColor.FromArgb(a.Color.A, a.Color.R, a.Color.G, a.Color.B);
                var brush = new XSolidBrush(color);
                var pen = new XPen(color, a.StrokeThickness);

                switch (a.Kind)
                {
                    case AnnotationKind.Highlight:
                        var hlColor = XColor.FromArgb(96, a.Color.R, a.Color.G, a.Color.B);
                        gfx.DrawRectangle(new XSolidBrush(hlColor), a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        break;

                    case AnnotationKind.Whiteout:
                        gfx.DrawRectangle(XBrushes.White, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        break;

                    case AnnotationKind.Redaction:
                        gfx.DrawRectangle(XBrushes.Black, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        break;

                    case AnnotationKind.Image:
                        if (!string.IsNullOrEmpty(a.ImagePath) && System.IO.File.Exists(a.ImagePath))
                        {
                            using var img = XImage.FromFile(a.ImagePath);
                            gfx.DrawImage(img, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        }
                        break;

                    case AnnotationKind.Rectangle:
                        if (a.Filled)
                        {
                            var fillBrush = new XSolidBrush(XColor.FromArgb(a.Color.A, a.Color.R, a.Color.G, a.Color.B));
                            gfx.DrawRectangle(fillBrush, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        }
                        else
                        {
                            gfx.DrawRectangle(pen, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        }
                        break;

                    case AnnotationKind.Ellipse:
                        if (a.Filled)
                        {
                            var fillBrush = new XSolidBrush(XColor.FromArgb(a.Color.A, a.Color.R, a.Color.G, a.Color.B));
                            gfx.DrawEllipse(fillBrush, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        }
                        else
                        {
                            gfx.DrawEllipse(pen, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        }
                        break;

                    case AnnotationKind.Ink:
                        if (a.InkPoints.Count > 1)
                        {
                            var pts = a.InkPoints.Select(p => new XPoint(p.X * w, p.Y * h)).ToArray();
                            for (int i = 1; i < pts.Length; i++)
                                gfx.DrawLine(pen, pts[i - 1], pts[i]);
                        }
                        break;

                    case AnnotationKind.StickyNote:
                        {
                            // Store as a proper PDF Text Annotation (Acrobat-style comment).
                            // Viewers show a small icon; hover / click reveals the note text.
                            // Because it's an annotation (not page content), it can be hidden at print time.
                            var noteText = a.Text ?? "";
                            // PDF coordinate system: origin is bottom-left, we're working top-left.
                            var xPt = a.X * w;
                            var yPtTop = a.Y * h;
                            var iconSize = 20.0;
                            var pdfRect = new PdfSharpCore.Pdf.PdfRectangle(
                                new XRect(xPt, page.Height.Point - yPtTop - iconSize, iconSize, iconSize));
                            var textAnn = new PdfSharpCore.Pdf.Annotations.PdfTextAnnotation
                            {
                                Title = "Note",
                                Subject = "Comment",
                                Contents = noteText,
                                Rectangle = pdfRect,
                                Color = XColors.Gold,
                                Open = false,
                                Icon = PdfSharpCore.Pdf.Annotations.PdfTextAnnotationIcon.Note
                            };
                            page.Annotations.Add(textAnn);
                        }
                        break;

                    case AnnotationKind.TextStamp:
                        if (!string.IsNullOrEmpty(a.Text))
                        {
                            var fam = string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily;
                            var size = a.FontSize > 0 ? a.FontSize : (a.Height * h > 8 ? a.Height * h : 14);
                            var style = XFontStyle.Regular;
                            if (a.Bold && a.Italic) style = XFontStyle.BoldItalic;
                            else if (a.Bold) style = XFontStyle.Bold;
                            else if (a.Italic) style = XFontStyle.Italic;
                            var font = new XFont(fam, size, style);
                            var lineHeight = font.GetHeight();
                            var maxW = (a.Width > 0.001 ? a.Width : 0.4) * w;
                            var lines = WrapText(a.Text ?? "", font, gfx, maxW);
                            var baselineY = a.Y * h + size;
                            var leftX = a.X * w;
                            for (int i = 0; i < lines.Count; i++)
                            {
                                var line = lines[i];
                                var isLastOfParagraph = i == lines.Count - 1
                                    || (i + 1 < lines.Count && string.IsNullOrWhiteSpace(lines[i + 1]));
                                DrawAlignedLine(gfx, line, font, brush, color, leftX, baselineY, maxW, size, a.Align, a.Underline, isLastOfParagraph);
                                baselineY += lineHeight;
                            }
                        }
                        break;
                }
            }
        }

        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Returns a copy of the PDF with sticky notes (/Text) and their popups (/Popup)
    /// removed. Hyperlinks (/Link), form fields (/Widget), and other annotation kinds are
    /// preserved so the print keeps its interactive elements intact.</summary>
    public byte[] StripAnnotations(byte[] pdfBytes)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify);
        foreach (var page in doc.Pages)
        {
            try
            {
                if (!page.Elements.ContainsKey("/Annots")) continue;
                var annots = page.Elements.GetArray("/Annots");
                if (annots == null) continue;
                for (int i = annots.Elements.Count - 1; i >= 0; i--)
                {
                    var dict = ResolveDict(annots.Elements[i]);
                    if (dict == null) continue;
                    var subtype = dict.Elements.GetName("/Subtype");
                    if (subtype == "/Text" || subtype == "/Popup") annots.Elements.RemoveAt(i);
                }
                if (annots.Elements.Count == 0) page.Elements.Remove("/Annots");
            }
            catch { }
        }
        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Returns true if any page has a sticky-note (/Text subtype) annotation —
    /// the only unflattened annotation kind PDF Editor writes.</summary>
    public bool HasStickyNotes(byte[] pdfBytes)
    {
        try
        {
            using var ms = new MemoryStream(pdfBytes);
            var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
            foreach (var page in doc.Pages)
            {
                if (!page.Elements.ContainsKey("/Annots")) continue;
                var annots = page.Elements.GetArray("/Annots");
                if (annots == null) continue;
                foreach (var el in annots.Elements)
                {
                    var dict = ResolveDict(el);
                    if (dict?.Elements.GetName("/Subtype") == "/Text") return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static PdfSharpCore.Pdf.PdfDictionary? ResolveDict(PdfSharpCore.Pdf.PdfItem item)
    {
        if (item is PdfSharpCore.Pdf.PdfDictionary d) return d;
        if (item is PdfSharpCore.Pdf.Advanced.PdfReference r) return r.Value as PdfSharpCore.Pdf.PdfDictionary;
        return null;
    }

    private static void DrawAlignedLine(XGraphics gfx, string line, XFont font, XBrush brush, XColor color,
                                        double leftX, double baselineY, double maxW, double fontSize,
                                        PDFEditor.Models.TextAlign align, bool underline, bool isLastOfParagraph)
    {
        if (string.IsNullOrEmpty(line)) return;
        var lineW = gfx.MeasureString(line, font).Width;

        double drawX = leftX;
        switch (align)
        {
            case PDFEditor.Models.TextAlign.Center:
                drawX = leftX + (maxW - lineW) / 2;
                break;
            case PDFEditor.Models.TextAlign.Right:
                drawX = leftX + (maxW - lineW);
                break;
            case PDFEditor.Models.TextAlign.Justify:
                if (!isLastOfParagraph)
                {
                    // Distribute extra space between words.
                    var words = line.Split(' ');
                    if (words.Length > 1)
                    {
                        double wordsW = 0;
                        foreach (var wrd in words) wordsW += gfx.MeasureString(wrd, font).Width;
                        var spaceW = gfx.MeasureString(" ", font).Width;
                        var totalSpaceNeeded = maxW - wordsW;
                        var perGap = totalSpaceNeeded / (words.Length - 1);
                        double x = leftX;
                        for (int wi = 0; wi < words.Length; wi++)
                        {
                            gfx.DrawString(words[wi], font, brush, new XPoint(x, baselineY));
                            x += gfx.MeasureString(words[wi], font).Width + perGap;
                        }
                        if (underline)
                        {
                            var underlineY = baselineY + fontSize * 0.12;
                            gfx.DrawLine(new XPen(color, System.Math.Max(1.0, fontSize * 0.07)), leftX, underlineY, leftX + maxW, underlineY);
                        }
                        return;
                    }
                }
                break;
        }
        gfx.DrawString(line, font, brush, new XPoint(drawX, baselineY));
        if (underline)
        {
            var underlineY = baselineY + fontSize * 0.12;
            gfx.DrawLine(new XPen(color, System.Math.Max(1.0, fontSize * 0.07)), drawX, underlineY, drawX + lineW, underlineY);
        }
    }

    private static System.Collections.Generic.List<string> WrapText(string text, XFont font, XGraphics gfx, double maxWidth)
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var words = raw.Split(' ');
            var cur = "";
            foreach (var w in words)
            {
                var candidate = string.IsNullOrEmpty(cur) ? w : cur + " " + w;
                var size = gfx.MeasureString(candidate, font);
                if (size.Width <= maxWidth || string.IsNullOrEmpty(cur))
                {
                    cur = candidate;
                }
                else
                {
                    lines.Add(cur); cur = w;
                }
            }
            if (!string.IsNullOrEmpty(cur)) lines.Add(cur);
        }
        return lines;
    }
}
