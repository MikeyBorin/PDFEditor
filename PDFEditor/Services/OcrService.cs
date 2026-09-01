using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PDFtoImage;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Tesseract;

namespace PDFEditor.Services;

/// <summary>
/// Rasterizes a page then OCRs it with Tesseract. Requires tessdata/eng.traineddata
/// alongside the executable (see README). Silently no-ops if tessdata is missing.
/// </summary>
public class OcrService
{
    private readonly string _tessDataPath;

    public OcrService()
    {
        _tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    /// <summary>True if any Tesseract training data file is installed. Callers
    /// that need a specific language should use <see cref="IsLanguageInstalled"/>.</summary>
    public bool IsAvailable => Directory.Exists(_tessDataPath) &&
                               Directory.EnumerateFiles(_tessDataPath, "*.traineddata").Any();

    public bool IsLanguageInstalled(string languageCode) =>
        File.Exists(Path.Combine(_tessDataPath, $"{languageCode}.traineddata"));

    public string OcrPage(byte[] pdfBytes, int pageIndex, int dpi = 300, string language = "eng")
    {
        if (!IsLanguageInstalled(language))
            return $"[OCR unavailable] Place '{language}.traineddata' in the app's 'tessdata' folder.";

        using var imgMs = new MemoryStream();
        Conversion.SavePng(imgMs, pdfBytes, page: pageIndex, options: new RenderOptions(Dpi: dpi));
        imgMs.Position = 0;

        using var engine = new TesseractEngine(_tessDataPath, language, EngineMode.Default);
        using var img = Pix.LoadFromMemory(imgMs.ToArray());
        using var res = engine.Process(img);
        return res.GetText();
    }

    public string OcrAllPages(byte[] pdfBytes, int dpi = 300, IProgress<int>? progress = null, string language = "eng")
    {
        if (!IsLanguageInstalled(language))
            return $"[OCR unavailable] Place '{language}.traineddata' in the app's 'tessdata' folder.";

        var count = Conversion.GetPageCount(pdfBytes);
        var sb = new StringBuilder();
        using var engine = new TesseractEngine(_tessDataPath, language, EngineMode.Default);
        for (int i = 0; i < count; i++)
        {
            using var imgMs = new MemoryStream();
            Conversion.SavePng(imgMs, pdfBytes, page: i, options: new RenderOptions(Dpi: dpi));
            using var img = Pix.LoadFromMemory(imgMs.ToArray());
            using var res = engine.Process(img);
            sb.AppendLine($"--- Page {i + 1} ---");
            sb.AppendLine(res.GetText());
            sb.AppendLine();
            progress?.Report(i + 1);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds a searchable PDF: rasterizes each page, drops the image on a new page,
    /// then overlays OCR text at ~1pt with alpha=1/255 so it's invisible but selectable/searchable.
    /// </summary>
    public byte[] BuildSearchablePdf(byte[] pdfBytes, int dpi = 200, IProgress<int>? progress = null, string language = "eng")
    {
        if (!IsLanguageInstalled(language))
            throw new InvalidOperationException($"OCR unavailable — install tessdata/{language}.traineddata.");

        var count = Conversion.GetPageCount(pdfBytes);
        var dst = new PdfDocument();
        using var engine = new TesseractEngine(_tessDataPath, language, EngineMode.Default);

        // Copy the source page sizes so overlay text lines up with the rendered image.
        using var srcMs = new MemoryStream(pdfBytes);
        var src = PdfReader.Open(srcMs, PdfDocumentOpenMode.Import);

        for (int i = 0; i < count; i++)
        {
            using var imgMs = new MemoryStream();
            Conversion.SavePng(imgMs, pdfBytes, page: i, options: new RenderOptions(Dpi: dpi));

            var srcPage = src.Pages[i];
            var page = dst.AddPage();
            page.Width = srcPage.Width;
            page.Height = srcPage.Height;

            imgMs.Position = 0;
            using var xImg = XImage.FromStream(() => imgMs);
            using var g = XGraphics.FromPdfPage(page);
            g.DrawImage(xImg, 0, 0, page.Width.Point, page.Height.Point);

            using var pix = Pix.LoadFromMemory(imgMs.ToArray());
            using var res = engine.Process(pix);
            using var iter = res.GetIterator();
            iter.Begin();

            // Nearly invisible black brush — alpha=1/255. Still in the content stream, so searchable.
            var invisible = new XSolidBrush(XColor.FromArgb(1, 0, 0, 0));
            var pxToPt = 72.0 / dpi;

            do
            {
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                {
                    var word = iter.GetText(PageIteratorLevel.Word) ?? "";
                    word = word.Trim();
                    if (word.Length == 0) continue;
                    var x = rect.X1 * pxToPt;
                    var y = rect.Y1 * pxToPt;
                    var wPt = (rect.X2 - rect.X1) * pxToPt;
                    var hPt = (rect.Y2 - rect.Y1) * pxToPt;
                    var fontSize = Math.Max(6, hPt * 0.9);
                    var font = new XFont("Arial", fontSize, XFontStyle.Regular);
                    g.DrawString(word, font, invisible, new XRect(x, y, wPt, hPt), XStringFormats.TopLeft);
                }
            } while (iter.Next(PageIteratorLevel.Word));

            progress?.Report(i + 1);
        }

        using var outMs = new MemoryStream();
        dst.Save(outMs, false);
        return outMs.ToArray();
    }
}
