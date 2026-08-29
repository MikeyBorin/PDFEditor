using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

public class PageOperationsService
{
    public byte[] CropAllPages(byte[] pdfBytes, double leftPt, double rightPt, double topPt, double bottomPt)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        foreach (var page in doc.Pages)
        {
            var mediaBox = page.MediaBox;
            var newLeft = mediaBox.X1 + leftPt;
            var newBottom = mediaBox.Y1 + bottomPt;
            var newRight = mediaBox.X2 - rightPt;
            var newTop = mediaBox.Y2 - topPt;
            if (newRight <= newLeft || newTop <= newBottom) continue;
            var crop = new PdfSharpCore.Pdf.PdfRectangle(new PdfSharpCore.Drawing.XPoint(newLeft, newBottom),
                                                          new PdfSharpCore.Drawing.XPoint(newRight, newTop));
            page.MediaBox = crop;
            page.CropBox = crop;
        }
        return SaveToBytes(doc);
    }

    public byte[] Rotate(byte[] pdfBytes, int pageIndex, int degrees)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[pageIndex];
        page.Rotate = (page.Rotate + degrees) % 360;
        if (page.Rotate < 0) page.Rotate += 360;
        return SaveToBytes(doc);
    }

    public byte[] DeletePages(byte[] pdfBytes, IEnumerable<int> pageIndexes)
    {
        using var input = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        foreach (var i in pageIndexes.OrderByDescending(x => x))
        {
            if (i >= 0 && i < doc.PageCount) doc.Pages.RemoveAt(i);
        }
        return SaveToBytes(doc);
    }

    public byte[] Reorder(byte[] pdfBytes, IList<int> newOrder)
    {
        using var input = new MemoryStream(pdfBytes);
        var src = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var dst = new PdfDocument();
        foreach (var idx in newOrder)
        {
            dst.AddPage(src.Pages[idx]);
        }
        return SaveToBytes(dst);
    }

    public byte[] Extract(byte[] pdfBytes, int startIndex, int endIndex)
    {
        using var input = new MemoryStream(pdfBytes);
        var src = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var dst = new PdfDocument();
        for (int i = startIndex; i <= endIndex && i < src.PageCount; i++)
            dst.AddPage(src.Pages[i]);
        return SaveToBytes(dst);
    }

    public byte[] Merge(IEnumerable<byte[]> pdfs)
    {
        var dst = new PdfDocument();
        foreach (var bytes in pdfs)
        {
            using var ms = new MemoryStream(bytes);
            var src = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            foreach (var page in src.Pages) dst.AddPage(page);
        }
        return SaveToBytes(dst);
    }

    public byte[] InsertPagesFromFile(byte[] targetPdf, string insertPdfPath, int atIndex)
    {
        var insertBytes = File.ReadAllBytes(insertPdfPath);
        using var tMs = new MemoryStream(targetPdf);
        var target = PdfReader.Open(tMs, PdfDocumentOpenMode.Import);
        using var iMs = new MemoryStream(insertBytes);
        var insert = PdfReader.Open(iMs, PdfDocumentOpenMode.Import);

        var dst = new PdfDocument();
        for (int i = 0; i < target.PageCount; i++)
        {
            if (i == atIndex)
            {
                foreach (var p in insert.Pages) dst.AddPage(p);
            }
            dst.AddPage(target.Pages[i]);
        }
        if (atIndex >= target.PageCount)
        {
            foreach (var p in insert.Pages) dst.AddPage(p);
        }
        return SaveToBytes(dst);
    }

    public List<byte[]> Split(byte[] pdfBytes, int pagesPerFile)
    {
        using var input = new MemoryStream(pdfBytes);
        var src = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var chunks = new List<byte[]>();
        for (int i = 0; i < src.PageCount; i += pagesPerFile)
        {
            var dst = new PdfDocument();
            for (int j = i; j < Math.Min(i + pagesPerFile, src.PageCount); j++)
                dst.AddPage(src.Pages[j]);
            chunks.Add(SaveToBytes(dst));
        }
        return chunks;
    }

    private static byte[] SaveToBytes(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }
}
