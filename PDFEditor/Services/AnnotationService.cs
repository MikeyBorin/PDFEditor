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
                        // Form widgets (checkboxes, text fields, etc.) are drawn by the
                        // viewer's form layer ON TOP of page content — a plain white
                        // rectangle can't cover them. Strip any widget whose centre
                        // falls inside this whiteout so "whiteout over a checkbox"
                        // actually removes the checkbox.
                        RemoveWidgetsUnderRegion(page, doc, a.X, a.Y, a.Width, a.Height);
                        break;

                    case AnnotationKind.Redaction:
                        gfx.DrawRectangle(XBrushes.Black, a.X * w, a.Y * h, a.Width * w, a.Height * h);
                        break;

                    case AnnotationKind.Image:
                        if (!string.IsNullOrEmpty(a.ImagePath) && System.IO.File.Exists(a.ImagePath))
                        {
                            // Use stream-based loading so PdfSharpCore takes the
                            // ImageSharp code path that preserves PNG alpha (soft mask).
                            // XImage.FromFile on Windows goes via GDI+ and can flatten
                            // transparency, which shows up as an opaque white background
                            // behind a saved signature.
                            var bytes = System.IO.File.ReadAllBytes(a.ImagePath);
                            using var img = XImage.FromStream(() => new MemoryStream(bytes));
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

                    case AnnotationKind.CheckboxField:
                        // NOT flattened as page content — this becomes a real
                        // interactive AcroForm checkbox field so it can be
                        // clicked in Adobe Reader and picked up by the
                        // "Check All Boxes" tool.
                        AddAcroCheckbox(page, a.X, a.Y, a.Width, a.Height);
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
                            if (a.FontWeight != 400)  underlying.SetInteger("/ArtiMaxNoteWeight", a.FontWeight);
                            if (a.Bold)               underlying.SetBoolean("/ArtiMaxNoteBold", true);   // legacy readers
                            if (a.Italic)    underlying.SetBoolean("/ArtiMaxNoteItalic", true);
                            if (a.Underline) underlying.SetBoolean("/ArtiMaxNoteUnderline", true);
                            underlying.SetName("/ArtiMaxNoteAlign", "/" + a.Align.ToString());
                            // Word-style effects: write only when set (keeps the dict tidy
                            // for notes that don't use them).
                            if (a.Strikethrough)        underlying.SetBoolean("/ArtiMaxStrikethrough", true);
                            if (a.DoubleStrikethrough)  underlying.SetBoolean("/ArtiMaxDoubleStrikethrough", true);
                            if (a.Superscript)          underlying.SetBoolean("/ArtiMaxSuperscript", true);
                            if (a.Subscript)            underlying.SetBoolean("/ArtiMaxSubscript", true);
                            if (a.SmallCaps)            underlying.SetBoolean("/ArtiMaxSmallCaps", true);
                            if (a.AllCaps)              underlying.SetBoolean("/ArtiMaxAllCaps", true);
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
                            // AllCaps / SmallCaps transform on the callout text. Super/sub
                            // and strikethrough don't have a native /FreeText representation,
                            // so only caps carries through the visible /Contents. To keep
                            // "toggle caps off" working on reopen, the untransformed original
                            // is stashed in /ArtiMaxOriginalText when caps was applied.
                            var originalText = a.Text ?? "";
                            var calloutText = (a.AllCaps || a.SmallCaps) ? originalText.ToUpper() : originalText;
                            ft.Elements.SetString("/Contents", calloutText);
                            if ((a.AllCaps || a.SmallCaps) && originalText != calloutText)
                                ft.Elements.SetString("/ArtiMaxOriginalText", originalText);
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
                            // Word-style effects — same keys as StickyNote so the load path
                            // can use one shared reader for both.
                            if (a.Strikethrough)       ft.Elements.SetBoolean("/ArtiMaxStrikethrough", true);
                            if (a.DoubleStrikethrough) ft.Elements.SetBoolean("/ArtiMaxDoubleStrikethrough", true);
                            if (a.Superscript)         ft.Elements.SetBoolean("/ArtiMaxSuperscript", true);
                            if (a.Subscript)           ft.Elements.SetBoolean("/ArtiMaxSubscript", true);
                            if (a.SmallCaps)           ft.Elements.SetBoolean("/ArtiMaxSmallCaps", true);
                            if (a.AllCaps)             ft.Elements.SetBoolean("/ArtiMaxAllCaps", true);
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
                            var baseSize = a.FontSize > 0 ? a.FontSize : (a.Height * h > 8 ? a.Height * h : 14);
                            // Super/subscript: shrink type to 70% of base size.
                            var size = (a.Superscript || a.Subscript) ? System.Math.Max(6, baseSize * 0.7) : baseSize;
                            // PdfSharpCore's XFontStyle is coarse — Regular / Bold / Italic
                            // / BoldItalic. Medium (500) and SemiBold (600) collapse to Bold
                            // on flatten because "slightly-heavier" is closer to Bold than
                            // to Regular. Weights below 400 collapse to Regular.
                            bool isBold = a.FontWeight >= 550;
                            var style = XFontStyle.Regular;
                            if (isBold && a.Italic) style = XFontStyle.BoldItalic;
                            else if (isBold)        style = XFontStyle.Bold;
                            else if (a.Italic)      style = XFontStyle.Italic;
                            var font = new XFont(fam, size, style);
                            var lineHeight = font.GetHeight();
                            var maxW = (a.Width > 0.001 ? a.Width : 0.4) * w;
                            // Super/subscript baseline shift, relative to the base size.
                            var baselineY = a.Y * h + baseSize
                                + (a.Superscript ? -baseSize * 0.35 : 0)
                                + (a.Subscript   ?  baseSize * 0.20 : 0);
                            var leftX = a.X * w;
                            // AllCaps folds to uppercase before rendering. SmallCaps keeps
                            // the original case string and renders per-char with two sizes.
                            var textForFlatten = a.AllCaps ? (a.Text ?? "").ToUpper() : (a.Text ?? "");
                            if (a.SmallCaps && !a.AllCaps)
                            {
                                // Proper small-caps flow: per-run render with a full-size and
                                // a 78% font. WrapText's single-font measurement is close enough
                                // (small-caps runs are slightly narrower than pure-uppercase),
                                // so lines only slightly under-fill.
                                var smallFont = new XFont(fam, System.Math.Max(6, size * 0.78), style);
                                var upperForWrap = (a.Text ?? "").ToUpper();
                                var lines = WrapText(upperForWrap, font, gfx, maxW);
                                // Optional background fill.
                                if (a.BackgroundColor is System.Windows.Media.Color bgCol0)
                                {
                                    var bgBrush = new XSolidBrush(XColor.FromArgb(bgCol0.R, bgCol0.G, bgCol0.B));
                                    var bgH = System.Math.Max(size, lineHeight * lines.Count) + 4;
                                    gfx.DrawRectangle(bgBrush, leftX - 2, a.Y * h - 2, maxW + 4, bgH);
                                }
                                // Walk the original (case-preserving) source in parallel with
                                // the wrapped-uppercase lines so we know which chars were lower.
                                int srcIdx = 0;
                                for (int li = 0; li < lines.Count; li++)
                                {
                                    var line = lines[li];
                                    var isLastOfParagraph = li == lines.Count - 1
                                        || (li + 1 < lines.Count && string.IsNullOrWhiteSpace(lines[li + 1]));
                                    DrawSmallCapsLine(gfx, line, font, smallFont, brush, color,
                                        leftX, baselineY, maxW, size, a.Align, a.Underline,
                                        a.Strikethrough, a.DoubleStrikethrough,
                                        a.Text ?? "", ref srcIdx);
                                    baselineY += lineHeight;
                                }
                            }
                            else
                            {
                                var lines = WrapText(textForFlatten, font, gfx, maxW);
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
                                    DrawAlignedLine(gfx, line, font, brush, color, leftX, baselineY, maxW, size, a.Align, a.Underline, isLastOfParagraph, a.Strikethrough, a.DoubleStrikethrough);
                                    baselineY += lineHeight;
                                }
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
                    // Prefer explicit numeric weight; fall back to the legacy Bold bool for
                    // notes saved before we introduced the fine-weight system.
                    int weight = 400;
                    if (dict.Elements.ContainsKey("/ArtiMaxNoteWeight"))
                        weight = dict.Elements.GetInteger("/ArtiMaxNoteWeight");
                    else if (dict.Elements.GetBoolean("/ArtiMaxNoteBold"))
                        weight = 700;
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
                        FontWeight = weight,
                        Italic = ital,
                        Underline = uline,
                        Align = align,
                        BackgroundColor = noteBg,
                        Strikethrough        = dict.Elements.GetBoolean("/ArtiMaxStrikethrough"),
                        DoubleStrikethrough  = dict.Elements.GetBoolean("/ArtiMaxDoubleStrikethrough"),
                        Superscript          = dict.Elements.GetBoolean("/ArtiMaxSuperscript"),
                        Subscript            = dict.Elements.GetBoolean("/ArtiMaxSubscript"),
                        SmallCaps            = dict.Elements.GetBoolean("/ArtiMaxSmallCaps"),
                        AllCaps              = dict.Elements.GetBoolean("/ArtiMaxAllCaps"),
                    });
                    annots.Elements.RemoveAt(i);
                }
                else if (subtype == "/FreeText" && IsArtiMaxCallout(dict))
                {
                    // Our callout — reify as an editable overlay Callout.
                    // Prefer the untransformed /ArtiMaxOriginalText when caps were applied,
                    // so toggling caps off in the edit dialog restores the original case.
                    var contents = dict.Elements.ContainsKey("/ArtiMaxOriginalText")
                        ? ReadString(dict, "/ArtiMaxOriginalText")
                        : ReadString(dict, "/Contents");
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
                        BackgroundColor = calloutBg,
                        Strikethrough        = dict.Elements.GetBoolean("/ArtiMaxStrikethrough"),
                        DoubleStrikethrough  = dict.Elements.GetBoolean("/ArtiMaxDoubleStrikethrough"),
                        Superscript          = dict.Elements.GetBoolean("/ArtiMaxSuperscript"),
                        Subscript            = dict.Elements.GetBoolean("/ArtiMaxSubscript"),
                        SmallCaps            = dict.Elements.GetBoolean("/ArtiMaxSmallCaps"),
                        AllCaps              = dict.Elements.GetBoolean("/ArtiMaxAllCaps"),
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
                                        PDFEditor.Models.TextAlign align, bool underline, bool isLastOfParagraph,
                                        bool strike = false, bool doubleStrike = false)
    {
        if (string.IsNullOrEmpty(line)) return;
        var lineW = gfx.MeasureString(line, font).Width;

        double drawX = leftX;
        double justSpanW = lineW;   // width to underline / strike across
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
                        DrawStrikeLines(gfx, color, leftX, leftX + maxW, baselineY, fontSize, strike, doubleStrike);
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
        DrawStrikeLines(gfx, color, drawX, drawX + lineW, baselineY, fontSize, strike, doubleStrike);
    }

    /// <summary>Renders a text line in Small Caps: originally-lowercase chars
    /// are drawn at the smaller size, originally-uppercase / non-letter chars
    /// at the full size — all rendered as uppercase glyphs. The `srcIdx` cursor
    /// walks the original case-preserving source in parallel with the wrapped
    /// upper-case `line`, so we know which chars used to be lowercase.</summary>
    private static void DrawSmallCapsLine(XGraphics gfx, string line, XFont fullFont, XFont smallFont,
                                          XBrush brush, XColor color,
                                          double leftX, double baselineY, double maxW, double fontSize,
                                          PDFEditor.Models.TextAlign align, bool underline,
                                          bool strike, bool doubleStrike,
                                          string originalSource, ref int srcIdx)
    {
        if (string.IsNullOrEmpty(line)) { srcIdx += line?.Length ?? 0; return; }

        // Skip any whitespace in the source that was consumed by wrapping
        // between lines (e.g. a space at a wrap boundary).
        while (srcIdx < originalSource.Length && originalSource[srcIdx] != line[0]
               && char.IsWhiteSpace(originalSource[srcIdx]))
            srcIdx++;

        // Group into runs by "originally lowercase or not".
        var runs = new System.Collections.Generic.List<(string Text, bool IsSmall)>();
        {
            int i = 0;
            while (i < line.Length)
            {
                var srcChar = (srcIdx + i) < originalSource.Length ? originalSource[srcIdx + i] : line[i];
                bool isSmall = char.IsLower(srcChar);
                int j = i + 1;
                while (j < line.Length)
                {
                    var sc = (srcIdx + j) < originalSource.Length ? originalSource[srcIdx + j] : line[j];
                    if (char.IsLower(sc) != isSmall) break;
                    j++;
                }
                runs.Add((line.Substring(i, j - i), isSmall));
                i = j;
            }
        }
        srcIdx += line.Length;

        // Total width so we can align.
        double totalW = 0;
        foreach (var (t, s) in runs) totalW += gfx.MeasureString(t, s ? smallFont : fullFont).Width;

        double drawX = leftX;
        switch (align)
        {
            case PDFEditor.Models.TextAlign.Center: drawX = leftX + (maxW - totalW) / 2; break;
            case PDFEditor.Models.TextAlign.Right:  drawX = leftX + (maxW - totalW); break;
        }
        var startX = drawX;
        foreach (var (t, isSmall) in runs)
        {
            var runFont = isSmall ? smallFont : fullFont;
            gfx.DrawString(t, runFont, brush, new XPoint(drawX, baselineY));
            drawX += gfx.MeasureString(t, runFont).Width;
        }
        if (underline)
        {
            var underlineY = baselineY + fontSize * 0.12;
            gfx.DrawLine(new XPen(color, System.Math.Max(1.0, fontSize * 0.07)), startX, underlineY, startX + totalW, underlineY);
        }
        DrawStrikeLines(gfx, color, startX, startX + totalW, baselineY, fontSize, strike, doubleStrike);
    }

    /// <summary>Draws a single (Strikethrough) or double (DoubleStrikethrough)
    /// horizontal line across the span. Both flags together render as double
    /// (the wider gesture wins).</summary>
    private static void DrawStrikeLines(XGraphics gfx, XColor color, double x1, double x2, double baselineY, double fontSize,
                                        bool strike, bool doubleStrike)
    {
        if (!strike && !doubleStrike) return;
        var pen = new XPen(color, System.Math.Max(0.8, fontSize * 0.06));
        // Strikethrough hovers a bit above the baseline through the x-height mid.
        var y = baselineY - fontSize * 0.30;
        if (doubleStrike)
        {
            var gap = System.Math.Max(1.0, fontSize * 0.10);
            gfx.DrawLine(pen, x1, y - gap / 2, x2, y - gap / 2);
            gfx.DrawLine(pen, x1, y + gap / 2, x2, y + gap / 2);
        }
        else
        {
            gfx.DrawLine(pen, x1, y, x2, y);
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

    // PdfSharpCore keeps PdfAcroForm and PdfCheckBoxField's document ctors
    // internal, but the underlying dictionaries are plain PDF dicts. Cached
    // reflection lets us instantiate them without maintaining a fork.
    private static readonly System.Reflection.ConstructorInfo _ctorAcroForm =
        typeof(PdfSharpCore.Pdf.AcroForms.PdfAcroForm).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(PdfSharpCore.Pdf.PdfDocument) }, null)!;

    /// <summary>Strips any /Widget annotation whose /Rect centre falls inside
    /// the given normalized-page-coord region. Also removes the corresponding
    /// entry from doc.AcroForm.Fields so the field truly disappears — not just
    /// its visible widget. Called from the Whiteout flatten path so drawing a
    /// whiteout over a form checkbox actually removes the checkbox.</summary>
    private static void RemoveWidgetsUnderRegion(PdfSharpCore.Pdf.PdfPage page,
                                                 PdfSharpCore.Pdf.PdfDocument doc,
                                                 double xNorm, double yNorm,
                                                 double wNorm, double hNorm)
    {
        if (!page.Elements.ContainsKey("/Annots")) return;
        var annots = page.Elements.GetArray("/Annots");
        if (annots == null) return;
        var pageW = page.Width.Point;
        var pageH = page.Height.Point;
        // Whiteout in PDF space (bottom-left origin).
        var xLo = xNorm * pageW;
        var xHi = (xNorm + wNorm) * pageW;
        var yHi = pageH - yNorm * pageH;                       // upper-right y
        var yLo = pageH - (yNorm + hNorm) * pageH;             // lower-left  y

        // Collect widget indirect refs we need to also purge from AcroForm.Fields.
        var removed = new System.Collections.Generic.List<PdfSharpCore.Pdf.PdfItem>();
        for (int i = annots.Elements.Count - 1; i >= 0; i--)
        {
            var el = annots.Elements[i];
            var dict = ResolveDict(el);
            if (dict == null) continue;
            if (dict.Elements.GetName("/Subtype") != "/Widget") continue;
            if (!dict.Elements.ContainsKey("/Rect")) continue;
            var rect = dict.Elements.GetRectangle("/Rect");
            var cx = (rect.X1 + rect.X2) / 2.0;
            var cy = (rect.Y1 + rect.Y2) / 2.0;
            if (cx >= xLo && cx <= xHi && cy >= yLo && cy <= yHi)
            {
                removed.Add(el);
                annots.Elements.RemoveAt(i);
            }
        }
        if (removed.Count == 0) return;

        // Purge matching entries from AcroForm.Fields as well.
        var form = doc.AcroForm;
        if (form == null) return;
        if (!form.Elements.ContainsKey("/Fields")) return;
        var fields = form.Elements.GetArray("/Fields");
        if (fields == null) return;
        // Compare by ObjectID for indirect refs, or by reference equality
        // for inline dicts (rare — widget fields are usually indirect).
        var removedIds = new System.Collections.Generic.HashSet<PdfSharpCore.Pdf.PdfObjectID>();
        foreach (var el in removed)
        {
            if (el is PdfSharpCore.Pdf.Advanced.PdfReference r)
                removedIds.Add(r.ObjectID);
        }
        for (int i = fields.Elements.Count - 1; i >= 0; i--)
        {
            var el = fields.Elements[i];
            if (el is PdfSharpCore.Pdf.Advanced.PdfReference r && removedIds.Contains(r.ObjectID))
                fields.Elements.RemoveAt(i);
            else if (removed.Contains(el))
                fields.Elements.RemoveAt(i);
        }
    }

    /// <summary>Writes an interactive AcroForm checkbox onto the given page at
    /// the normalized rectangle (top-left origin, y-down). Adds the widget to
    /// the page's /Annots array and registers it as a field in doc.AcroForm.
    /// Provides /AP appearance streams for the Off and Yes states so viewers
    /// (including PDFium, which doesn't reliably honour /NeedAppearances)
    /// render the box border and the tick without needing to author their
    /// own appearances.</summary>
    private static void AddAcroCheckbox(PdfSharpCore.Pdf.PdfPage page,
                                        double xNorm, double yNorm,
                                        double wNorm, double hNorm)
    {
        var doc = page.Owner;
        var pageW = page.Width.Point;
        var pageH = page.Height.Point;

        // Convert normalized top-left rect to PDF-space bottom-left rect.
        var x1 = xNorm * pageW;
        var x2 = (xNorm + wNorm) * pageW;
        var y1 = pageH - (yNorm + hNorm) * pageH;
        var y2 = pageH - yNorm * pageH;
        var boxW = System.Math.Max(1, x2 - x1);
        var boxH = System.Math.Max(1, y2 - y1);

        // Ensure /AcroForm exists on the catalog.
        var form = doc.AcroForm;
        if (form == null)
        {
            form = (PdfSharpCore.Pdf.AcroForms.PdfAcroForm)_ctorAcroForm.Invoke(new object[] { doc });
            doc.Internals.AddObject(form);
            doc.Internals.Catalog.Elements["/AcroForm"] = form.Reference;
        }
        form.Elements["/NeedAppearances"] = new PdfSharpCore.Pdf.PdfBoolean(true);

        // Auto-generate a unique field name.
        var existing = new System.Collections.Generic.HashSet<string>(
            form.Fields.Names, System.StringComparer.Ordinal);
        int n = 1;
        while (existing.Contains($"cb_{n}")) n++;
        var fieldName = $"cb_{n}";

        // Raw widget/field dictionary — PdfSharpCore reads /FT /Btn on load and
        // materialises it as a PdfCheckBoxField, so we don't need the typed class.
        var cb = new PdfSharpCore.Pdf.PdfDictionary(doc);
        doc.Internals.AddObject(cb);
        cb.Elements.SetName("/Type", "/Annot");
        cb.Elements.SetName("/Subtype", "/Widget");
        cb.Elements.SetName("/FT", "/Btn");
        cb.Elements.SetString("/T", fieldName);
        cb.Elements.SetName("/V", "/Off");
        cb.Elements.SetName("/AS", "/Off");
        cb.Elements["/Rect"] = new PdfSharpCore.Pdf.PdfArray(doc,
            new PdfSharpCore.Pdf.PdfReal(x1), new PdfSharpCore.Pdf.PdfReal(y1),
            new PdfSharpCore.Pdf.PdfReal(x2), new PdfSharpCore.Pdf.PdfReal(y2));
        cb.Elements["/P"] = page.Reference;
        cb.Elements.SetInteger("/F", 4); // Print flag

        // Thin black border via /BS + /MK — this alone gives most viewers a
        // visible empty box, and it's what /AP's Off state also draws.
        var bs = new PdfSharpCore.Pdf.PdfDictionary(doc);
        bs.Elements.SetInteger("/W", 1);
        bs.Elements.SetName("/S", "/S");
        cb.Elements["/BS"] = bs;
        var mk = new PdfSharpCore.Pdf.PdfDictionary(doc);
        mk.Elements["/BC"] = new PdfSharpCore.Pdf.PdfArray(doc,
            new PdfSharpCore.Pdf.PdfReal(0), new PdfSharpCore.Pdf.PdfReal(0), new PdfSharpCore.Pdf.PdfReal(0));
        cb.Elements["/MK"] = mk;

        // --- Appearance streams for Off (empty box border) and Yes (box + tick). ---
        // PDF content stream operators used here:
        //   q ... Q       : save / restore graphics state
        //   w             : line width
        //   RG            : stroke colour (RGB, 0..1)
        //   re            : rectangle
        //   S             : stroke (outline)
        //   m / l         : moveto / lineto
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string F(double d) => d.ToString("0.###", inv);

        // Off — draw the border outline within the BBox.
        var offContent = System.Text.Encoding.ASCII.GetBytes(
            $"q 0 0 0 RG 1 w {F(0.5)} {F(0.5)} {F(boxW - 1)} {F(boxH - 1)} re S Q\n");
        var apOff = new PdfSharpCore.Pdf.PdfDictionary(doc);
        apOff.Elements.SetName("/Type", "/XObject");
        apOff.Elements.SetName("/Subtype", "/Form");
        apOff.Elements["/BBox"] = new PdfSharpCore.Pdf.PdfArray(doc,
            new PdfSharpCore.Pdf.PdfReal(0), new PdfSharpCore.Pdf.PdfReal(0),
            new PdfSharpCore.Pdf.PdfReal(boxW), new PdfSharpCore.Pdf.PdfReal(boxH));
        apOff.Elements["/Resources"] = new PdfSharpCore.Pdf.PdfDictionary(doc);
        apOff.CreateStream(offContent);
        doc.Internals.AddObject(apOff);

        // Yes — same border, plus a check mark (three vector line segments).
        var onContent = System.Text.Encoding.ASCII.GetBytes(
            $"q 0 0 0 RG 1 w {F(0.5)} {F(0.5)} {F(boxW - 1)} {F(boxH - 1)} re S " +
            $"{F(System.Math.Max(1.2, boxW * 0.10))} w " +
            $"{F(boxW * 0.20)} {F(boxH * 0.50)} m " +
            $"{F(boxW * 0.42)} {F(boxH * 0.25)} l " +
            $"{F(boxW * 0.80)} {F(boxH * 0.75)} l S Q\n");
        var apOn = new PdfSharpCore.Pdf.PdfDictionary(doc);
        apOn.Elements.SetName("/Type", "/XObject");
        apOn.Elements.SetName("/Subtype", "/Form");
        apOn.Elements["/BBox"] = new PdfSharpCore.Pdf.PdfArray(doc,
            new PdfSharpCore.Pdf.PdfReal(0), new PdfSharpCore.Pdf.PdfReal(0),
            new PdfSharpCore.Pdf.PdfReal(boxW), new PdfSharpCore.Pdf.PdfReal(boxH));
        apOn.Elements["/Resources"] = new PdfSharpCore.Pdf.PdfDictionary(doc);
        apOn.CreateStream(onContent);
        doc.Internals.AddObject(apOn);

        var apN = new PdfSharpCore.Pdf.PdfDictionary(doc);
        apN.Elements["/Off"] = apOff.Reference;
        apN.Elements["/Yes"] = apOn.Reference;
        var ap = new PdfSharpCore.Pdf.PdfDictionary(doc);
        ap.Elements["/N"] = apN;
        cb.Elements["/AP"] = ap;

        // Wire the widget onto the page's /Annots and the form's /Fields.
        var annots = page.Elements.GetArray("/Annots");
        if (annots == null)
        {
            annots = new PdfSharpCore.Pdf.PdfArray(doc);
            page.Elements["/Annots"] = annots;
        }
        annots.Elements.Add(cb.Reference);

        var fields = form.Elements.GetArray("/Fields");
        if (fields == null)
        {
            fields = new PdfSharpCore.Pdf.PdfArray(doc);
            form.Elements["/Fields"] = fields;
        }
        fields.Elements.Add(cb.Reference);
    }
}
