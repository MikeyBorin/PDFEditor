using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig;

namespace PDFEditor.Services;

public record PageCompareResult(int PageIndex, bool SameText, string TextA, string TextB, int LinesAdded, int LinesRemoved);

public class CompareService
{
    public List<PageCompareResult> ComparePages(byte[] pdfA, byte[] pdfB)
    {
        var pagesA = ExtractPages(pdfA);
        var pagesB = ExtractPages(pdfB);
        var count = System.Math.Max(pagesA.Count, pagesB.Count);
        var results = new List<PageCompareResult>();
        for (int i = 0; i < count; i++)
        {
            var a = i < pagesA.Count ? pagesA[i] : "";
            var b = i < pagesB.Count ? pagesB[i] : "";
            var same = a == b;
            var (added, removed) = same ? (0, 0) : LineCounts(a, b);
            results.Add(new PageCompareResult(i, same, a, b, added, removed));
        }
        return results;
    }

    private static List<string> ExtractPages(byte[] pdfBytes)
    {
        var list = new List<string>();
        using var ms = new MemoryStream(pdfBytes);
        using var pdf = PdfDocument.Open(ms);
        foreach (var p in pdf.GetPages()) list.Add(p.Text);
        return list;
    }

    /// <summary>Simple line-count diff — how many lines exist in one but not the other.</summary>
    private static (int added, int removed) LineCounts(string a, string b)
    {
        var la = a.Split('\n').Select(l => l.TrimEnd('\r')).ToHashSet();
        var lb = b.Split('\n').Select(l => l.TrimEnd('\r')).ToHashSet();
        var added = lb.Except(la).Count();
        var removed = la.Except(lb).Count();
        return (added, removed);
    }
}
