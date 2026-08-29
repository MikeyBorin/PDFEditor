using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PDFEditor.Services;

public class WordExportService
{
    private readonly ExtractService _extract;

    public WordExportService(ExtractService extract) { _extract = extract; }

    /// <summary>True if Microsoft Word is installed (via COM). Used for high-fidelity export.</summary>
    public bool IsWordAvailable => Type.GetTypeFromProgID("Word.Application") != null;

    public async Task<string> ExportAsync(byte[] pdfBytes, string? sourcePath, string outputPath, IProgress<string>? progress = null)
    {
        if (IsWordAvailable)
        {
            progress?.Report("Converting via Microsoft Word (high fidelity)...");
            var tempPdf = sourcePath ?? Path.Combine(Path.GetTempPath(), $"pdfeditor-{Guid.NewGuid():N}.pdf");
            var createdTemp = false;
            if (sourcePath == null || !File.Exists(sourcePath))
            {
                await File.WriteAllBytesAsync(tempPdf, pdfBytes);
                createdTemp = true;
            }
            try
            {
                await RunOnStaThread(() => ExportViaWord(tempPdf, outputPath));
                return "Word";
            }
            finally
            {
                if (createdTemp && File.Exists(tempPdf)) try { File.Delete(tempPdf); } catch { }
            }
        }

        progress?.Report("Converting via text extraction (basic — layout not preserved)...");
        await Task.Run(() => ExportViaOpenXml(pdfBytes, outputPath));
        return "OpenXml";
    }

    private static Task RunOnStaThread(Action action)
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

    private static void ExportViaWord(string pdfPath, string docxPath)
    {
        var wordType = Type.GetTypeFromProgID("Word.Application")
            ?? throw new InvalidOperationException("Word.Application ProgID not found.");
        dynamic? word = null;
        dynamic? doc = null;
        try
        {
            word = Activator.CreateInstance(wordType);
            word!.Visible = false;
            word.DisplayAlerts = 0; // wdAlertsNone

            // Open(FileName, ConfirmConversions, ReadOnly, ...)
            doc = word.Documents.Open(pdfPath, false, false);
            const int wdFormatDocumentDefault = 16; // .docx
            doc.SaveAs2(docxPath, wdFormatDocumentDefault);
            doc.Close(false);
            doc = null;
        }
        finally
        {
            try { word?.Quit(); } catch { }
            if (doc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(doc);
            if (word != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(word);
        }
    }

    /// <summary>Opens the docx in a visible Word instance for user editing. Does not block.</summary>
    public static void OpenDocxInWord(string docxPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(docxPath) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }

    /// <summary>Converts an edited .docx back to PDF via Word COM, returning the PDF bytes.</summary>
    public async Task<byte[]> DocxToPdfBytesAsync(string docxPath)
    {
        if (!IsWordAvailable) throw new InvalidOperationException("Microsoft Word is required for this operation.");
        var tempPdf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pdfeditor-{Guid.NewGuid():N}.pdf");
        try
        {
            await RunOnStaThread(() => DocxToPdfViaWord(docxPath, tempPdf));
            return await File.ReadAllBytesAsync(tempPdf);
        }
        finally
        {
            if (File.Exists(tempPdf)) try { File.Delete(tempPdf); } catch { }
        }
    }

    private static void DocxToPdfViaWord(string docxPath, string pdfPath)
    {
        var wordType = Type.GetTypeFromProgID("Word.Application")
            ?? throw new InvalidOperationException("Word.Application ProgID not found.");
        dynamic? word = null;
        dynamic? doc = null;
        try
        {
            word = Activator.CreateInstance(wordType);
            word!.Visible = false;
            word.DisplayAlerts = 0;
            doc = word.Documents.Open(docxPath, false, true); // ReadOnly=true
            const int wdFormatPDF = 17;
            doc.SaveAs2(pdfPath, wdFormatPDF);
            doc.Close(false);
            doc = null;
        }
        finally
        {
            try { word?.Quit(); } catch { }
            if (doc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(doc);
            if (word != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(word);
        }
    }

    private void ExportViaOpenXml(byte[] pdfBytes, string docxPath)
    {
        var text = _extract.ExtractAllText(pdfBytes);
        using var docx = WordprocessingDocument.Create(docxPath, WordprocessingDocumentType.Document);
        var main = docx.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        var body = main.Document.Body!;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var para = new W.Paragraph();
            if (trimmed.StartsWith("--- Page "))
            {
                var props = new W.ParagraphProperties(
                    new W.ParagraphStyleId { Val = "Heading2" });
                para.PrependChild(props);
            }
            var run = new W.Run(new W.Text(trimmed) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(run);
            body.AppendChild(para);
        }

        main.Document.Save();
    }
}
