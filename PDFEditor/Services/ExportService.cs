using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PDFtoImage;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace PDFEditor.Services;

public class ExportService
{
    private readonly ExtractService _extract;
    public ExportService(ExtractService extract) { _extract = extract; }

    public List<string> ExportPagesAsPng(byte[] pdfBytes, string directory, string stem, int dpi = 200)
    {
        Directory.CreateDirectory(directory);
        var count = Conversion.GetPageCount(pdfBytes);
        var files = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var path = Path.Combine(directory, $"{stem}-{i + 1:000}.png");
            using var fs = File.Create(path);
            Conversion.SavePng(fs, pdfBytes, page: i, options: new RenderOptions(Dpi: dpi));
            files.Add(path);
        }
        return files;
    }

    public List<string> ExportPagesAsJpeg(byte[] pdfBytes, string directory, string stem, int dpi = 200)
    {
        Directory.CreateDirectory(directory);
        var count = Conversion.GetPageCount(pdfBytes);
        var files = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var path = Path.Combine(directory, $"{stem}-{i + 1:000}.jpg");
            using var fs = File.Create(path);
            Conversion.SaveJpeg(fs, pdfBytes, page: i, options: new RenderOptions(Dpi: dpi));
            files.Add(path);
        }
        return files;
    }

    public void ExportAsHtml(byte[] pdfBytes, string outputPath, string? title = null)
    {
        var text = _extract.ExtractAllText(pdfBytes);
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>")
          .Append(System.Net.WebUtility.HtmlEncode(title ?? "PDF Export"))
          .AppendLine("</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;max-width:840px;margin:2em auto;padding:0 1em;line-height:1.5;color:#222;}");
        sb.AppendLine("h2{border-bottom:1px solid #ccc;padding-bottom:.2em;margin-top:2em;}");
        sb.AppendLine(".page{page-break-after:always;}</style></head><body>");

        var pageSplit = Regex.Split(text, @"^--- Page \d+ ---\r?$", RegexOptions.Multiline);
        int pageNum = 0;
        foreach (var chunk in pageSplit)
        {
            var t = chunk.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            pageNum++;
            sb.Append("<section class=\"page\"><h2>Page ").Append(pageNum).Append("</h2>");
            foreach (var line in t.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(trimmed)) sb.Append("<p></p>");
                else sb.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(trimmed)).Append("</p>");
            }
            sb.Append("</section>");
        }
        sb.AppendLine("</body></html>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Basic .xlsx: one sheet per PDF page, each text line becomes one row in column A.
    /// Not a real table extractor — a starting point for further Excel work.
    /// </summary>
    public void ExportAsXlsx(byte[] pdfBytes, string outputPath)
    {
        var text = _extract.ExtractAllText(pdfBytes);
        using var doc = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);
        var workbookPart = doc.AddWorkbookPart();
        workbookPart.Workbook = new S.Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());

        var pageChunks = Regex.Split(text, @"^--- Page \d+ ---\r?$", RegexOptions.Multiline);
        uint sheetId = 1;
        int pageNum = 0;
        foreach (var chunk in pageChunks)
        {
            var t = chunk.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            pageNum++;

            var wsPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            wsPart.Worksheet = new S.Worksheet(sheetData);

            uint row = 1;
            foreach (var line in t.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (string.IsNullOrEmpty(trimmed)) continue;
                var cell = new S.Cell
                {
                    CellReference = $"A{row}",
                    DataType = S.CellValues.String,
                    CellValue = new S.CellValue(trimmed)
                };
                sheetData.Append(new S.Row(cell) { RowIndex = row });
                row++;
            }

            sheets.Append(new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(wsPart),
                SheetId = sheetId++,
                Name = $"Page {pageNum}"
            });
        }

        workbookPart.Workbook.Save();
    }
}
