using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PDFEditor.Services;

/// <summary>
/// Translates text via MyMemory's public API (api.mymemory.translated.net).
/// No API key required — plain HTTPS GET. Chunks text under the ~500-char query
/// limit and stitches the pieces back together, preserving paragraph breaks.
/// Free-tier daily quota is roughly 10k characters/IP anonymously; on quota the
/// service returns a plain-text warning that we surface as an exception.
/// </summary>
public class TranslateService
{
    private const string Endpoint = "https://api.mymemory.translated.net/get";
    // MyMemory's practical limit is 500 chars per `q` param. Stay under.
    private const int ChunkChars = 480;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>Common language codes supported by MyMemory. Not exhaustive — MyMemory
    /// accepts most ISO-639-1 codes, but this list covers the everyday ones.</summary>
    public static readonly (string Code, string Name)[] Languages = new (string, string)[]
    {
        ("en", "English"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("nl", "Dutch"),
        ("pl", "Polish"),
        ("ru", "Russian"),
        ("tr", "Turkish"),
        ("sv", "Swedish"),
        ("da", "Danish"),
        ("no", "Norwegian"),
        ("fi", "Finnish"),
        ("cs", "Czech"),
        ("el", "Greek"),
        ("he", "Hebrew"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
        ("zh-CN", "Chinese (Simplified)"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("vi", "Vietnamese"),
        ("th", "Thai"),
        ("id", "Indonesian"),
    };

    public async Task<string> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        IProgress<(int done, int total)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase)) return text;

        var chunks = ChunkText(text, ChunkChars);
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < chunks.Count; i++)
        {
            var translated = await TranslateChunkAsync(chunks[i], sourceLang, targetLang, ct);
            sb.Append(translated);
            progress?.Report((i + 1, chunks.Count));
        }
        return sb.ToString();
    }

    private static async Task<string> TranslateChunkAsync(string chunk, string src, string tgt, CancellationToken ct)
    {
        // Omit the &de= developer-email parameter deliberately — MyMemory validates it as
        // a real email address and rejects anything else. Anonymous requests use the
        // ~10 KB/day/IP tier, which is what we want.
        var url = $"{Endpoint}?q={Uri.EscapeDataString(chunk)}&langpair={Uri.EscapeDataString(src)}|{Uri.EscapeDataString(tgt)}";
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Detect quota-exhausted response. MyMemory returns responseStatus != 200 or
        // responseDetails starting with "MYMEMORY WARNING" when the free quota is used up.
        if (root.TryGetProperty("responseStatus", out var status) && status.ValueKind == JsonValueKind.Number)
        {
            var code = status.GetInt32();
            if (code != 200)
            {
                var details = root.TryGetProperty("responseDetails", out var dd) ? dd.GetString() : null;
                throw new InvalidOperationException($"MyMemory error ({code}): {details ?? "unknown"}");
            }
        }

        var translated = root
            .GetProperty("responseData")
            .GetProperty("translatedText")
            .GetString() ?? "";

        // MyMemory returns HTML-escaped text ("don&#39;t" → "don't"). Decode.
        return WebUtility.HtmlDecode(translated);
    }

    /// <summary>Break text into chunks under maxLen chars. Prefers paragraph
    /// boundaries; falls back to word boundaries; last resort hard split. Preserves
    /// paragraph structure in the output by keeping the "\n\n" separators.</summary>
    private static List<string> ChunkText(string text, int maxLen)
    {
        var chunks = new List<string>();
        // Normalize line endings so paragraph split is consistent across platforms.
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
        var buffer = new StringBuilder();

        void FlushBuffer()
        {
            if (buffer.Length > 0) { chunks.Add(buffer.ToString()); buffer.Clear(); }
        }

        foreach (var raw in paragraphs)
        {
            var para = raw;
            if (para.Length == 0) continue;

            if (para.Length > maxLen)
            {
                FlushBuffer();
                foreach (var piece in SplitByWords(para, maxLen))
                {
                    chunks.Add(piece);
                }
                continue;
            }

            // Room to append to current buffer with a paragraph break?
            int need = para.Length + (buffer.Length > 0 ? 2 : 0);
            if (buffer.Length + need <= maxLen)
            {
                if (buffer.Length > 0) buffer.Append("\n\n");
                buffer.Append(para);
            }
            else
            {
                FlushBuffer();
                buffer.Append(para);
            }
        }
        FlushBuffer();
        return chunks;
    }

    private static IEnumerable<string> SplitByWords(string text, int maxLen)
    {
        int i = 0;
        while (i < text.Length)
        {
            int remaining = text.Length - i;
            int take = Math.Min(remaining, maxLen);
            int end = i + take;
            // Try to break at the last space so we don't cut a word in half.
            if (end < text.Length)
            {
                int lastSpace = text.LastIndexOf(' ', end - 1, end - i);
                if (lastSpace > i) end = lastSpace + 1;
            }
            yield return text.Substring(i, end - i);
            i = end;
        }
    }
}
