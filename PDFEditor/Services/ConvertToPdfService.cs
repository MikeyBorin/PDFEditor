using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace PDFEditor.Services;

public class ConvertToPdfService
{
    private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
    private static readonly string[] TextExts = { ".txt", ".log", ".csv", ".xml", ".json", ".md" };

    /// <summary>Which apps are available on this machine (probed once).</summary>
    public bool WordAvailable => Type.GetTypeFromProgID("Word.Application") != null;
    public bool ExcelAvailable => Type.GetTypeFromProgID("Excel.Application") != null;
    public bool PowerPointAvailable => Type.GetTypeFromProgID("PowerPoint.Application") != null;
    public bool OutlookAvailable => Type.GetTypeFromProgID("Outlook.Application") != null;

    /// <summary>
    /// Returns a one-word string describing the method used ("Word", "Excel", "PowerPoint", "Outlook", "Image", "Text", "PDF").
    /// Throws if the format isn't supported or if the required COM app is missing.
    /// </summary>
    public async Task<string> ConvertAsync(string sourcePath, string outputPath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        // Word documents
        if (ext is ".doc" or ".docx" or ".rtf" or ".odt")
        {
            if (!WordAvailable) throw new InvalidOperationException("Microsoft Word is required to convert " + ext);
            await RunOnSta(() => ConvertViaWord(sourcePath, outputPath));
            return "Word";
        }

        // Excel workbooks (and Word can open HTML but Excel is the natural fit for CSV workbooks)
        if (ext is ".xls" or ".xlsx" or ".xlsm" or ".ods")
        {
            if (!ExcelAvailable) throw new InvalidOperationException("Microsoft Excel is required to convert " + ext);
            await RunOnSta(() => ConvertViaExcel(sourcePath, outputPath));
            return "Excel";
        }

        // PowerPoint presentations
        if (ext is ".ppt" or ".pptx" or ".odp")
        {
            if (!PowerPointAvailable) throw new InvalidOperationException("Microsoft PowerPoint is required to convert " + ext);
            await RunOnSta(() => ConvertViaPowerPoint(sourcePath, outputPath));
            return "PowerPoint";
        }

        // Outlook email
        if (ext is ".msg" or ".oft")
        {
            if (!OutlookAvailable) throw new InvalidOperationException("Microsoft Outlook is required to convert " + ext);
            await RunOnSta(() => ConvertViaOutlook(sourcePath, outputPath));
            return "Outlook";
        }

        // HTML — Word does a good job when present
        if (ext is ".html" or ".htm" or ".mht" or ".mhtml")
        {
            if (WordAvailable) { await RunOnSta(() => ConvertViaWord(sourcePath, outputPath)); return "Word"; }
            throw new InvalidOperationException("HTML conversion currently requires Word.");
        }

        // Images (single-page for now — multi-page TIFF becomes one page)
        if (Array.IndexOf(ImageExts, ext) >= 0)
        {
            await Task.Run(() => ConvertImage(sourcePath, outputPath));
            return "Image";
        }

        // Plain text
        if (Array.IndexOf(TextExts, ext) >= 0)
        {
            await Task.Run(() => ConvertText(sourcePath, outputPath));
            return "Text";
        }

        // Already a PDF — just copy
        if (ext == ".pdf")
        {
            await Task.Run(() => File.Copy(sourcePath, outputPath, true));
            return "PDF";
        }

        throw new NotSupportedException("Unsupported source type: " + ext);
    }

    public async Task<List<(string SourcePath, string OutputPath, string Mode, string? Error)>> ConvertBatchAsync(
        IEnumerable<string> sources, string outputDirectory, IProgress<string>? progress = null)
    {
        var results = new List<(string, string, string, string?)>();
        Directory.CreateDirectory(outputDirectory);
        foreach (var src in sources)
        {
            var outPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(src) + ".pdf");
            progress?.Report($"Converting {Path.GetFileName(src)}...");
            try
            {
                var mode = await ConvertAsync(src, outPath);
                results.Add((src, outPath, mode, null));
            }
            catch (Exception ex)
            {
                results.Add((src, outPath, "", ex.Message));
            }
        }
        return results;
    }

    // --- Format-specific converters ---

    private static void ConvertViaWord(string src, string dst)
    {
        var t = Type.GetTypeFromProgID("Word.Application")!;
        dynamic? app = null, doc = null;
        try
        {
            app = Activator.CreateInstance(t)!;
            app.Visible = false;
            app.DisplayAlerts = 0;
            doc = app.Documents.Open(src, false, true);
            const int wdFormatPDF = 17;
            doc.SaveAs2(dst, wdFormatPDF);
            doc.Close(false); doc = null;
        }
        finally
        {
            try { app?.Quit(); } catch { }
            if (doc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(doc);
            if (app != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
        }
    }

    private static void ConvertViaExcel(string src, string dst)
    {
        var t = Type.GetTypeFromProgID("Excel.Application")!;
        dynamic? app = null, book = null;
        try
        {
            app = Activator.CreateInstance(t)!;
            app.Visible = false;
            app.DisplayAlerts = false;
            book = app.Workbooks.Open(src, 0, true);
            const int xlTypePDF = 0;
            book.ExportAsFixedFormat(xlTypePDF, dst);
            book.Close(false); book = null;
        }
        finally
        {
            try { app?.Quit(); } catch { }
            if (book != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(book);
            if (app != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
        }
    }

    private static void ConvertViaPowerPoint(string src, string dst)
    {
        var t = Type.GetTypeFromProgID("PowerPoint.Application")!;
        dynamic? app = null, pres = null;
        try
        {
            app = Activator.CreateInstance(t)!;
            // PowerPoint requires Visible to be true or MsoTriState.msoTrue at open time.
            pres = app.Presentations.Open(src, MsoTrue: -1, WithWindow: 0);
            const int ppSaveAsPDF = 32;
            pres.SaveAs(dst, ppSaveAsPDF, -1);
            pres.Close(); pres = null;
        }
        finally
        {
            try { app?.Quit(); } catch { }
            if (pres != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(pres);
            if (app != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
        }
    }

    private static void ConvertViaOutlook(string src, string dst)
    {
        // Route MSG through Word: Outlook saves the MSG's body via Word's "Save as PDF" support.
        var t = Type.GetTypeFromProgID("Outlook.Application")!;
        dynamic? app = null, item = null;
        try
        {
            app = Activator.CreateInstance(t)!;
            item = app.Session.OpenSharedItem(src);
            // Outlook 2016+ can SaveAs olSaveAsPDF (11).
            const int olSaveAsPDF = 11;
            try { item.SaveAs(dst, olSaveAsPDF); }
            catch
            {
                // Fallback: save as HTML then convert via Word.
                var tempHtml = Path.Combine(Path.GetTempPath(), $"msg-{Guid.NewGuid():N}.html");
                const int olHTML = 5;
                item.SaveAs(tempHtml, olHTML);
                ConvertViaWord(tempHtml, dst);
                try { File.Delete(tempHtml); } catch { }
            }
            item.Close(1); item = null;
        }
        finally
        {
            try { app?.Quit(); } catch { }
            if (item != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(item);
            if (app != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
        }
    }

    private static void ConvertImage(string src, string dst)
    {
        var doc = new PdfDocument();
        var page = doc.AddPage();
        using var img = XImage.FromFile(src);
        // Fit the image to the page while preserving aspect. Default US Letter portrait.
        page.Size = PdfSharpCore.PageSize.Letter;
        var pageW = page.Width.Point;
        var pageH = page.Height.Point;
        var scale = Math.Min(pageW / (double)img.PixelWidth, pageH / (double)img.PixelHeight);
        var w = img.PixelWidth * scale;
        var h = img.PixelHeight * scale;
        var x = (pageW - w) / 2;
        var y = (pageH - h) / 2;
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(img, x, y, w, h);
        doc.Save(dst);
    }

    private static void ConvertText(string src, string dst)
    {
        var lines = File.ReadAllLines(src);
        var doc = new PdfDocument();
        var font = new XFont("Consolas", 10, XFontStyle.Regular);
        var margin = 48.0;
        var lineHeight = font.GetHeight();
        PdfPage? page = null;
        XGraphics? gfx = null;
        double y = 0;
        double pageH = 0, pageW = 0;
        void NewPage()
        {
            page = doc.AddPage();
            page.Size = PdfSharpCore.PageSize.Letter;
            gfx = XGraphics.FromPdfPage(page);
            pageW = page.Width.Point;
            pageH = page.Height.Point;
            y = margin;
        }
        NewPage();
        foreach (var raw in lines)
        {
            if (y + lineHeight > pageH - margin) { gfx!.Dispose(); NewPage(); }
            gfx!.DrawString(raw ?? "", font, XBrushes.Black, new XPoint(margin, y + font.Size));
            y += lineHeight;
        }
        gfx?.Dispose();
        doc.Save(dst);
    }

    private static Task RunOnSta(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        var t = new Thread(() =>
        {
            try { action(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }
}
