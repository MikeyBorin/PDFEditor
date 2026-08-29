using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

public class BookmarkNode
{
    public string Title { get; set; } = "";
    public int PageIndex { get; set; } = -1;
    public List<BookmarkNode> Children { get; } = new();
}

public class BookmarkService
{
    public List<BookmarkNode> Read(byte[] pdfBytes)
    {
        var list = new List<BookmarkNode>();
        using var ms = new MemoryStream(pdfBytes);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        try
        {
            if (doc.Outlines != null)
            {
                foreach (var o in doc.Outlines) list.Add(Convert(o, doc));
            }
        }
        catch { /* some PDFs have malformed outlines; return what we've got */ }
        return list;
    }

    private BookmarkNode Convert(PdfOutline outline, PdfDocument doc)
    {
        var n = new BookmarkNode { Title = outline.Title ?? "" };
        try
        {
            var dest = outline.DestinationPage;
            if (dest != null)
            {
                for (int i = 0; i < doc.PageCount; i++)
                    if (doc.Pages[i].Reference == dest.Reference) { n.PageIndex = i; break; }
            }
        }
        catch { }

        try
        {
            foreach (var child in outline.Outlines) n.Children.Add(Convert(child, doc));
        }
        catch { }
        return n;
    }
}
