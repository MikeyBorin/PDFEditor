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
                            // Stash the user's styling in marker keys so ExtractAndStripStickyNotes
                            // can re-materialise the overlay with the original attributes (text
                            // colour, font, size, weight, style, alignment). Native /Text carries
                            // only Contents; without these markers the extracted overlay would
                            // fall back to default black text at 14pt.
                            var doc2 = page.Owner;
                            var underlying = textAnn.Elements;
                            var txR = a.Color.R / 255.0;
                            var txG = a.Color.G / 255.0;
                            var txB = a.Color.B / 255.0;
                            underlying["/ArtiMaxNoteColor"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                new PdfSharpCore.Pdf.PdfReal(txR), new PdfSharpCore.Pdf.PdfReal(txG), new PdfSharpCore.Pdf.PdfReal(txB));
                            if (a.BackgroundColor is System.Windows.Media.Color noteBg)
                            {
                                underlying["/ArtiMaxNoteBg"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                    new PdfSharpCore.Pdf.PdfReal(noteBg.R / 255.0),
                                    new PdfSharpCore.Pdf.PdfReal(noteBg.G / 255.0),
                                    new PdfSharpCore.Pdf.PdfReal(noteBg.B / 255.0));
                            }
                            if (!string.IsNullOrEmpty(a.FontFamily)) underlying.SetString("/ArtiMaxNoteFont", a.FontFamily);
                            if (a.FontSize > 0) underlying.SetReal("/ArtiMaxNoteSize", a.FontSize);
                            if (a.Bold)      underlying.SetBoolean("/ArtiMaxNoteBold", true);
                            if (a.Italic)    underlying.SetBoolean("/ArtiMaxNoteItalic", true);
                            if (a.Underline) underlying.SetBoolean("/ArtiMaxNoteUnderline", true);
                            underlying.SetName("/ArtiMaxNoteAlign", "/" + a.Align.ToString());
                        }
                        break;

                    case AnnotationKind.Callout:
                        {
                            // Save as a native PDF /FreeText annotation with callout intent —
                            // so it survives Save + reopen as a real callout in other viewers
                            // (Acrobat, Foxit, Chrome), and we round-trip it back to an editable
                            // overlay on Load via the /ArtiMaxCallout marker key.
                            var boxLeft = a.X * w;
                            var boxTop  = a.Y * h;
                            var boxW    = System.Math.Max(20, a.Width  * w);
                            var boxH    = System.Math.Max(16, a.Height * h);
                            var anchorX = a.AnchorX * w;
                            var anchorY = a.AnchorY * h;

                            // /Rect in PDF coords (bottom-left origin): [llx lly urx ury]
                            var pageH = page.Height.Point;
                            var llx = boxLeft;
                            var lly = pageH - (boxTop + boxH);
                            var urx = boxLeft + boxW;
                            var ury = pageH - boxTop;

                            // Callout line: tail = box-edge midpoint closest to anchor, head = anchor.
                            double cx = boxLeft + boxW / 2, cy = boxTop + boxH / 2;
                            double dx = anchorX - cx, dy = anchorY - cy;
                            double tailX, tailY;
                            if (System.Math.Abs(dx) * boxH > System.Math.Abs(dy) * boxW)
                            { tailX = dx > 0 ? boxLeft + boxW : boxLeft; tailY = cy; }
                            else
                            { tailX = cx; tailY = dy > 0 ? boxTop + boxH : boxTop; }

                            var fs = a.FontSize > 0 ? a.FontSize : 12;
                            var inv = System.Globalization.CultureInfo.InvariantCulture;
                            // /C on a /FreeText is interpreted by Acrobat/most viewers as the
                            // INTERIOR fill colour, not the border/text. Use the user's chosen
                            // background if they picked one; otherwise fall back to the historical
                            // yellow post-it look so callouts stay visible in external viewers.
                            // The text colour goes into /DA, and both text + bg go into /ArtiMax
                            // marker keys so ExtractAndStripStickyNotes restores them exactly.
                            double bgR = 1.0, bgG = 0.92, bgB = 0.51;
                            if (a.BackgroundColor is System.Windows.Media.Color cbc)
                            {
                                bgR = cbc.R / 255.0;
                                bgG = cbc.G / 255.0;
                                bgB = cbc.B / 255.0;
                            }
                            var txR = a.Color.R / 255.0;
                            var txG = a.Color.G / 255.0;
                            var txB = a.Color.B / 255.0;

                            var doc2 = page.Owner;
                            var ft = new PdfSharpCore.Pdf.PdfDictionary(doc2);
                            ft.Elements.SetName("/Type", "/Annot");
                            ft.Elements.SetName("/Subtype", "/FreeText");
                            ft.Elements["/Rect"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                new PdfSharpCore.Pdf.PdfReal(llx), new PdfSharpCore.Pdf.PdfReal(lly),
                                new PdfSharpCore.Pdf.PdfReal(urx), new PdfSharpCore.Pdf.PdfReal(ury));
                            ft.Elements.SetString("/Contents", a.Text ?? "");
                            ft.Elements.SetString("/DA",
                                $"/Helv {fs.ToString("0.##", inv)} Tf " +
                                $"{txR.ToString("0.##", inv)} {txG.ToString("0.##", inv)} {txB.ToString("0.##", inv)} rg");
                            ft.Elements.SetName("/IT", "/FreeTextCallout");
                            ft.Elements["/CL"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                new PdfSharpCore.Pdf.PdfReal(tailX), new PdfSharpCore.Pdf.PdfReal(pageH - tailY),
                                new PdfSharpCore.Pdf.PdfReal(anchorX), new PdfSharpCore.Pdf.PdfReal(pageH - anchorY));
                            ft.Elements.SetName("/LE", "/OpenArrow");
                            var bs = new PdfSharpCore.Pdf.PdfDictionary(doc2);
                            bs.Elements.SetInteger("/W", 1);
                            bs.Elements.SetName("/S", "/S");
                            ft.Elements["/BS"] = bs;
                            ft.Elements["/C"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                new PdfSharpCore.Pdf.PdfReal(bgR), new PdfSharpCore.Pdf.PdfReal(bgG), new PdfSharpCore.Pdf.PdfReal(bgB));
                            ft.Elements["/IC"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                new PdfSharpCore.Pdf.PdfReal(bgR), new PdfSharpCore.Pdf.PdfReal(bgG), new PdfSharpCore.Pdf.PdfReal(bgB));
                            ft.Elements.SetInteger("/F", 4); // Print flag
                            // Our marker + preserved user colour so ExtractAndStrip... can
                            // reify with the original colour rather than the yellow /C we wrote.
                            ft.Elements.SetBoolean("/ArtiMaxCallout", true);
                            ft.Elements["/ArtiMaxCalloutColor"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                new PdfSharpCore.Pdf.PdfReal(txR), new PdfSharpCore.Pdf.PdfReal(txG), new PdfSharpCore.Pdf.PdfReal(txB));
                            if (a.BackgroundColor is System.Windows.Media.Color cbg)
                            {
                                ft.Elements["/ArtiMaxCalloutBg"] = new PdfSharpCore.Pdf.PdfArray(doc2,
                                    new PdfSharpCore.Pdf.PdfReal(cbg.R / 255.0),
                                    new PdfSharpCore.Pdf.PdfReal(cbg.G / 255.0),
                                    new PdfSharpCore.Pdf.PdfReal(cbg.B / 255.0));
                            }

                            doc2.Internals.AddObject(ft);
                            var annots = page.Elements.GetArray("/Annots");
                            if (annots == null)
                            {
                                annots = new PdfSharpCore.Pdf.PdfArray(doc2);
                                page.Elements["/Annots"] = annots;
                            }
                            annots.Elements.Add(ft.Reference);
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
                            // Optional background fill: draw before the text so glyphs paint on top.
                            if (a.BackgroundColor is System.Windows.Media.Color bgCol)
                            {
                                var bgBrush = new XSolidBrush(XColor.FromArgb(bgCol.R, bgCol.G, bgCol.B));
                                var bgH = System.Math.Max(size, lineHeight * lines.Count) + 4;
                                gfx.DrawRectangle(bgBrush, leftX - 2, a.Y * h - 2, maxW + 4, bgH);
                            }
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

    /// <summary>Extracts every native /Text sticky-note annotation from the PDF, converts
    /// each into an editable overlay PdfAnnotation (Kind = StickyNote), and returns bytes
    /// with those /Text (and their paired /Popup) annotations removed. Called on Load and
    /// after Save so the round-trip is Layer 1 → native /Text on disk → Layer 1.</summary>
    public (byte[] cleanedBytes, System.Collections.Generic.List<Models.PdfAnnotation> notes) ExtractAndStripStickyNotes(byte[] pdfBytes)
    {
        var notes = new System.Collections.Generic.List<Models.PdfAnnotation>();
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify);
        for (int p = 0; p < doc.PageCount; p++)
        {
            var page = doc.Pages[p];
            if (!page.Elements.ContainsKey("/Annots")) continue;
            var annots = page.Elements.GetArray("/Annots");
            if (annots == null) continue;
            var pageW = page.Width.Point;
            var pageH = page.Height.Point;

            for (int i = annots.Elements.Count - 1; i >= 0; i--)
            {
                var dict = ResolveDict(annots.Elements[i]);
                if (dict == null) continue;
                var subtype = dict.Elements.GetName("/Subtype");
                if (subtype == "/Text")
                {
                    var contents = ReadString(dict, "/Contents");
                    var rect = dict.Elements.GetRectangle("/Rect");
                    var normX = pageW > 0 ? rect.X1 / pageW : 0;
                    // /Rect is (llx lly urx ury) in PDF bottom-left coords; ury is the icon top.
                    var normY = pageH > 0 ? 1.0 - (rect.Y2 / pageH) : 0;

                    // Read the marker keys the Save path wrote so the overlay comes
                    // back with the user's original text colour / font / style. If a
                    // note pre-dates the markers, fall back to black text at defaults.
                    var noteColor = System.Windows.Media.Colors.Black;
                    if (dict.Elements.TryGetValue("/ArtiMaxNoteColor", out var ncItem))
                    {
                        var arr = ncItem as PdfSharpCore.Pdf.PdfArray
                               ?? (ncItem as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfArray;
                        if (arr != null && arr.Elements.Count >= 3)
                        {
                            var r = (byte)System.Math.Clamp(arr.Elements.GetReal(0) * 255, 0, 255);
                            var g = (byte)System.Math.Clamp(arr.Elements.GetReal(1) * 255, 0, 255);
                            var b = (byte)System.Math.Clamp(arr.Elements.GetReal(2) * 255, 0, 255);
                            noteColor = System.Windows.Media.Color.FromRgb(r, g, b);
                        }
                    }
                    System.Windows.Media.Color? noteBg = null;
                    if (dict.Elements.TryGetValue("/ArtiMaxNoteBg", out var nbItem))
                    {
                        var arr = nbItem as PdfSharpCore.Pdf.PdfArray
                               ?? (nbItem as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfArray;
                        if (arr != null && arr.Elements.Count >= 3)
                        {
                            var r = (byte)System.Math.Clamp(arr.Elements.GetReal(0) * 255, 0, 255);
                            var g = (byte)System.Math.Clamp(arr.Elements.GetReal(1) * 255, 0, 255);
                            var b = (byte)System.Math.Clamp(arr.Elements.GetReal(2) * 255, 0, 255);
                            noteBg = System.Windows.Media.Color.FromRgb(r, g, b);
                        }
                    }
                    var fam   = dict.Elements.GetString("/ArtiMaxNoteFont");
                    var fsize = dict.Elements.ContainsKey("/ArtiMaxNoteSize") ? dict.Elements.GetReal("/ArtiMaxNoteSize") : 12.0;
                    var bold  = dict.Elements.GetBoolean("/ArtiMaxNoteBold");
                    var ital  = dict.Elements.GetBoolean("/ArtiMaxNoteItalic");
                    var uline = dict.Elements.GetBoolean("/ArtiMaxNoteUnderline");
                    var alignName = dict.Elements.GetName("/ArtiMaxNoteAlign");
                    var align = alignName switch
                    {
                        "/Center"  => Models.TextAlign.Center,
                        "/Right"   => Models.TextAlign.Right,
                        "/Justify" => Models.TextAlign.Justify,
                        _          => Models.TextAlign.Left
                    };

                    notes.Add(new Models.PdfAnnotation
                    {
                        PageIndex = p,
                        Kind = Models.AnnotationKind.StickyNote,
                        X = System.Math.Clamp(normX, 0, 1),
                        Y = System.Math.Clamp(normY, 0, 1),
                        Width = 0.03,
                        Height = 0.03,
                        Text = contents,
                        Color = noteColor,
                        FontFamily = string.IsNullOrEmpty(fam) ? "Arial" : fam,
                        FontSize = fsize > 0 ? fsize : 12,
                        Bold = bold,
                        Italic = ital,
                        Underline = uline,
                        Align = align,
                        BackgroundColor = noteBg
                    });
                    annots.Elements.RemoveAt(i);
                }
                else if (subtype == "/FreeText" && IsArtiMaxCallout(dict))
                {
                    // Our callout — reify as an editable overlay Callout.
                    var contents = ReadString(dict, "/Contents");
                    var rect = dict.Elements.GetRectangle("/Rect");
                    // Box position in normalized top-left coords
                    var boxX = pageW > 0 ? rect.X1 / pageW : 0;
                    var boxY = pageH > 0 ? 1.0 - (rect.Y2 / pageH) : 0;
                    var boxWn = pageW > 0 ? (rect.X2 - rect.X1) / pageW : 0.2;
                    var boxHn = pageH > 0 ? (rect.Y2 - rect.Y1) / pageH : 0.06;

                    // Anchor: head of the /CL callout line (last two coords), converted to top-left.
                    double anchorX = 0, anchorY = 0;
                    if (dict.Elements.TryGetValue("/CL", out var clItem))
                    {
                        var cl = clItem as PdfSharpCore.Pdf.PdfArray
                              ?? (clItem as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfArray;
                        if (cl != null && cl.Elements.Count >= 4)
                        {
                            var hxPdf = cl.Elements.GetReal(cl.Elements.Count - 2);
                            var hyPdf = cl.Elements.GetReal(cl.Elements.Count - 1);
                            anchorX = pageW > 0 ? hxPdf / pageW : 0;
                            anchorY = pageH > 0 ? 1.0 - (hyPdf / pageH) : 0;
                        }
                    }

                    // Colour: prefer the /ArtiMaxCalloutColor marker (preserves the user's
                    // exact original colour); fall back to /C (which we hard-code to yellow
                    // for Acrobat compatibility, so it's not useful as a user-colour source).
                    var callColor = System.Windows.Media.Colors.Black;
                    var colorKey = dict.Elements.ContainsKey("/ArtiMaxCalloutColor")
                        ? "/ArtiMaxCalloutColor" : "/C";
                    if (dict.Elements.TryGetValue(colorKey, out var cItem))
                    {
                        var c = cItem as PdfSharpCore.Pdf.PdfArray
                             ?? (cItem as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfArray;
                        if (c != null && c.Elements.Count >= 3)
                        {
                            var r = (byte)System.Math.Clamp(c.Elements.GetReal(0) * 255, 0, 255);
                            var g = (byte)System.Math.Clamp(c.Elements.GetReal(1) * 255, 0, 255);
                            var b = (byte)System.Math.Clamp(c.Elements.GetReal(2) * 255, 0, 255);
                            callColor = System.Windows.Media.Color.FromRgb(r, g, b);
                        }
                    }

                    System.Windows.Media.Color? calloutBg = null;
                    if (dict.Elements.TryGetValue("/ArtiMaxCalloutBg", out var cbItem))
                    {
                        var arr = cbItem as PdfSharpCore.Pdf.PdfArray
                               ?? (cbItem as PdfSharpCore.Pdf.Advanced.PdfReference)?.Value as PdfSharpCore.Pdf.PdfArray;
                        if (arr != null && arr.Elements.Count >= 3)
                        {
                            var r2 = (byte)System.Math.Clamp(arr.Elements.GetReal(0) * 255, 0, 255);
                            var g2 = (byte)System.Math.Clamp(arr.Elements.GetReal(1) * 255, 0, 255);
                            var b2 = (byte)System.Math.Clamp(arr.Elements.GetReal(2) * 255, 0, 255);
                            calloutBg = System.Windows.Media.Color.FromRgb(r2, g2, b2);
                        }
                    }

                    notes.Add(new Models.PdfAnnotation
                    {
                        PageIndex = p,
                        Kind = Models.AnnotationKind.Callout,
                        X = System.Math.Clamp(boxX, 0, 1),
                        Y = System.Math.Clamp(boxY, 0, 1),
                        Width = System.Math.Clamp(boxWn, 0.02, 1),
                        Height = System.Math.Clamp(boxHn, 0.02, 1),
                        AnchorX = System.Math.Clamp(anchorX, 0, 1),
                        AnchorY = System.Math.Clamp(anchorY, 0, 1),
                        Text = contents,
                        Color = callColor,
                        StrokeThickness = 1.5,
                        BackgroundColor = calloutBg
                    });
                    annots.Elements.RemoveAt(i);
                }
                else if (subtype == "/Popup")
                {
                    // Paired popup dictionaries — remove them too so no orphan references remain.
                    annots.Elements.RemoveAt(i);
                }
            }
            if (annots.Elements.Count == 0) page.Elements.Remove("/Annots");
        }
        using var output = new MemoryStream();
        doc.Save(output, false);
        return (output.ToArray(), notes);
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

    private static bool IsArtiMaxCallout(PdfSharpCore.Pdf.PdfDictionary dict)
    {
        if (!dict.Elements.TryGetValue("/ArtiMaxCallout", out var v)) return false;
        return v switch
        {
            PdfSharpCore.Pdf.PdfBoolean b => b.Value,
            PdfSharpCore.Pdf.PdfBooleanObject bo => bo.Value,
            _ => v?.ToString() == "true"
        };
    }

    private static string ReadString(PdfSharpCore.Pdf.PdfDictionary dict, string key)
    {
        if (!dict.Elements.TryGetValue(key, out var item)) return "";
        return item switch
        {
            PdfSharpCore.Pdf.PdfString s => s.Value,
            _ => item?.ToString() ?? ""
        };
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
