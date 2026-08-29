using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PDFEditor.Services;

public record SearchHit(int PageIndex, string Snippet, double NormX, double NormY, double NormW, double NormH);

public class ExtractService
{
    public string ExtractAllText(byte[] pdfBytes)
    {
        using var ms = new MemoryStream(pdfBytes);
        using var pdf = PdfDocument.Open(ms);
        var sb = new StringBuilder();
        foreach (Page page in pdf.GetPages())
        {
            sb.AppendLine($"--- Page {page.Number} ---");
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string ExtractPageText(byte[] pdfBytes, int pageIndex)
    {
        using var ms = new MemoryStream(pdfBytes);
        using var pdf = PdfDocument.Open(ms);
        return pdf.GetPage(pageIndex + 1).Text;
    }

    public record RegionText(string Text, double AverageFontHeightPoints, double FontPointSize, string FontFamily, bool Bold, bool Italic, string DebugInfo);

    /// <summary>Extracts words whose bounding boxes intersect the given normalized region.
    /// Coordinates are 0..1 with origin top-left (matches AnnotationLayer).</summary>
    public RegionText ExtractTextInRegion(byte[] pdfBytes, int pageIndex, double nx, double ny, double nw, double nh)
    {
        using var ms = new MemoryStream(pdfBytes);
        using var pdf = PdfDocument.Open(ms);
        var page = pdf.GetPage(pageIndex + 1);
        var pw = page.Width;
        var ph = page.Height;

        // Convert normalized top-left region → PdfPig bottom-left region.
        double left = nx * pw;
        double right = (nx + nw) * pw;
        double bottom = (1.0 - (ny + nh)) * ph;
        double top = (1.0 - ny) * ph;

        var hits = new List<UglyToad.PdfPig.Content.Word>();
        foreach (var word in page.GetWords())
        {
            var bb = word.BoundingBox;
            bool inside = bb.Right >= left && bb.Left <= right && bb.Top >= bottom && bb.Bottom <= top;
            if (inside) hits.Add(word);
        }

        if (hits.Count == 0) return new RegionText("", 10.0, 10.0, "Arial", false, false, "no hits");

        // Reading order: top→bottom, left→right; group into lines by y-band.
        var ordered = hits.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();
        var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
        var current = new List<UglyToad.PdfPig.Content.Word> { ordered[0] };
        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = current[^1];
            var w = ordered[i];
            if (System.Math.Abs(w.BoundingBox.Bottom - prev.BoundingBox.Bottom) < prev.BoundingBox.Height * 0.5)
                current.Add(w);
            else { lines.Add(current); current = new List<UglyToad.PdfPig.Content.Word> { w }; }
        }
        lines.Add(current);

        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var byX = line.OrderBy(w => w.BoundingBox.Left).ToList();
            for (int i = 0; i < byX.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(byX[i].Text);
            }
            sb.AppendLine();
        }

        var avgH = hits.Average(w => w.BoundingBox.Height);

        // Aggregate glyph-level info: PdfPig gives us Letter.PointSize, Letter.FontName, and
        // Letter.GlyphRectangle (actual on-page bounding box in points).
        var letters = hits.SelectMany(w => w.Letters).ToList();
        double pointSize = 12.0;
        string fontFamily = "Arial";
        bool bold = false, italic = false;
        if (letters.Count > 0)
        {
            // Candidate 1: reported Letter.PointSize (often correct; sometimes wrong because of
            // scaled text-matrix fonts defined at 1pt).
            var reported = letters.Select(l => l.PointSize).Where(s => s > 0 && s < 400).OrderBy(s => s).ToList();
            double reportedMedian = reported.Count > 0 ? reported[reported.Count / 2] : 0;

            // Candidate 2: derived from actual rendered glyph height.
            // For most Latin fonts, a full ascender-line-to-baseline glyph (e.g. capital letters)
            // takes ~0.72 × point size in PDF points. Take the tallest letters to approximate
            // cap-height and back-solve.
            var glyphHeights = letters.Select(l => l.GlyphRectangle.Height)
                                       .Where(h => h > 0.5 && h < 400)
                                       .OrderByDescending(h => h).Take(System.Math.Max(3, letters.Count / 4)).ToList();
            double derivedFromGlyph = 0;
            if (glyphHeights.Count > 0)
            {
                var capApprox = glyphHeights.Average();
                derivedFromGlyph = capApprox / 0.72;
            }

            // Candidate 3: derived from word bounding-box height ~= 1.15 × point size.
            double derivedFromBBox = avgH > 0 ? avgH / 1.15 : 0;

            // Bias toward the largest sane candidate. PdfPig sometimes reports scaled 1pt fonts,
            // and being visually too small on-page is a worse outcome than being slightly too big.
            var candidates = new[] { reportedMedian, derivedFromGlyph, derivedFromBBox }
                .Where(v => v > 0.5 && v < 400).OrderByDescending(v => v).ToList();
            if (candidates.Count > 0) pointSize = candidates[0];

            // Majority font name.
            var rawName = letters.Select(l => l.FontName ?? "")
                                 .Where(s => !string.IsNullOrEmpty(s))
                                 .GroupBy(s => s)
                                 .OrderByDescending(g => g.Count())
                                 .FirstOrDefault()?.Key ?? "";
            fontFamily = NormalizeFontName(rawName, out bold, out italic);
        }

        // For diagnostics — expose the three candidate calculations so the caller can display them.
        var debug = $"reported={reportedFor(letters)}pt, glyph={glyphCandFor(letters):0.0}pt, bbox={(avgH > 0 ? avgH / 1.15 : 0):0.0}pt, avgH={avgH:0.0}pt, picked={pointSize:0.0}pt, letters={letters.Count}";
        return new RegionText(sb.ToString().TrimEnd(), avgH, pointSize, fontFamily, bold, italic, debug);

        static string reportedFor(System.Collections.Generic.List<UglyToad.PdfPig.Content.Letter> ls)
        {
            var r = ls.Select(l => l.PointSize).Where(s => s > 0 && s < 400).OrderBy(s => s).ToList();
            return r.Count > 0 ? r[r.Count / 2].ToString("0.0") : "?";
        }
        static double glyphCandFor(System.Collections.Generic.List<UglyToad.PdfPig.Content.Letter> ls)
        {
            var h = ls.Select(l => l.GlyphRectangle.Height).Where(x => x > 0.5 && x < 400).OrderByDescending(x => x).Take(System.Math.Max(3, ls.Count / 4)).ToList();
            return h.Count > 0 ? h.Average() / 0.72 : 0;
        }
    }

    /// <summary>PDF font names often have prefixes like 'ABCDEF+Arial-BoldMT' or 'TimesNewRomanPSMT'.
    /// Strip the 6-char subset prefix and heuristically detect bold/italic in the suffix.</summary>
    private static string NormalizeFontName(string raw, out bool bold, out bool italic)
    {
        bold = false; italic = false;
        if (string.IsNullOrEmpty(raw)) return "Arial";
        // Strip subset prefix "ABCDEF+"
        var plus = raw.IndexOf('+');
        if (plus == 6) raw = raw.Substring(plus + 1);
        // Split on '-' and ',' to separate family name from style suffixes
        var parts = raw.Split(new[] { '-', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        var family = parts.Length > 0 ? parts[0] : raw;
        // Trim common suffixes
        foreach (var suffix in new[] { "PSMT", "MT", "PS", "Std" })
            if (family.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                family = family.Substring(0, family.Length - suffix.Length);
        // Detect bold/italic anywhere in the raw name
        var rawLower = raw.ToLowerInvariant();
        bold = rawLower.Contains("bold") || rawLower.Contains("black") || rawLower.Contains("heavy");
        italic = rawLower.Contains("italic") || rawLower.Contains("oblique");
        // Map a few common PDF names to Windows equivalents
        return family switch
        {
            "TimesNewRoman" or "TimesNewRomanPS" => "Times New Roman",
            "CourierNew" or "CourierNewPS" => "Courier New",
            "Helvetica" => "Arial",
            "" => "Arial",
            _ => family
        };
    }

    public List<SearchHit> Search(byte[] pdfBytes, string query, bool caseSensitive = false)
    {
        var results = new List<SearchHit>();
        if (string.IsNullOrEmpty(query)) return results;

        using var ms = new MemoryStream(pdfBytes);
        using var pdf = PdfDocument.Open(ms);
        var cmp = caseSensitive ? System.StringComparison.Ordinal : System.StringComparison.OrdinalIgnoreCase;

        foreach (Page page in pdf.GetPages())
        {
            var pageW = page.Width;
            var pageH = page.Height;
            var words = page.GetWords().ToList();
            var text = page.Text;
            int idx = 0;
            while ((idx = text.IndexOf(query, idx, cmp)) >= 0)
            {
                var start = System.Math.Max(0, idx - 30);
                var end = System.Math.Min(text.Length, idx + query.Length + 30);
                var snippet = text.Substring(start, end - start).Replace('\n', ' ');

                // Find matching word for coords (best-effort).
                var w = words.FirstOrDefault(wd => wd.Text.IndexOf(query, cmp) >= 0);
                if (w != null)
                {
                    var bb = w.BoundingBox;
                    // PdfPig uses PDF coords (origin bottom-left). Convert to top-left normalized.
                    var nx = bb.Left / pageW;
                    var ny = 1.0 - (bb.Top / pageH);
                    var nw = bb.Width / pageW;
                    var nh = bb.Height / pageH;
                    results.Add(new SearchHit(page.Number - 1, snippet, nx, ny, nw, nh));
                }
                else
                {
                    results.Add(new SearchHit(page.Number - 1, snippet, 0, 0, 0, 0));
                }
                idx += query.Length;
            }
        }
        return results;
    }
}
