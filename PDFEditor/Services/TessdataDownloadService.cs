using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PDFEditor.Services;

/// <summary>
/// Downloads Tesseract OCR training data from the official GitHub repository into the
/// app's tessdata folder. Writes to a .part file first and atomically moves on success
/// so a mid-download crash never leaves a truncated file that Tesseract would choke on.
/// </summary>
public class TessdataDownloadService
{
    // Combined LSTM + legacy tessdata (main repo). Larger (~22 MB) but broadest compatibility.
    // Alternatives: tessdata_best (accuracy-first), tessdata_fast (size/speed-first).
    private const string BaseUrl = "https://github.com/tesseract-ocr/tessdata/raw/main/";

    public string InstallDir => Path.Combine(AppContext.BaseDirectory, "tessdata");

    public string DestinationPath(string languageCode) =>
        Path.Combine(InstallDir, $"{languageCode}.traineddata");

    public bool IsInstalled(string languageCode) => File.Exists(DestinationPath(languageCode));

    public async Task DownloadAsync(
        string languageCode,
        IProgress<(long downloaded, long? total)>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(InstallDir);
        var destination = DestinationPath(languageCode);
        var temp = destination + ".part";

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ArtiMaxPDFEditor");
        // GitHub raw redirects; give it room to follow.
        http.Timeout = TimeSpan.FromMinutes(5);

        using var response = await http.GetAsync(
            BaseUrl + languageCode + ".traineddata",
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        long downloaded = 0;

        await using (var netStream = await response.Content.ReadAsStreamAsync(ct))
        await using (var fileStream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await netStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                progress?.Report((downloaded, totalBytes));
            }
        }

        // Sanity-check size: real eng.traineddata is >1 MB. A tiny file usually means
        // an error page slipped through despite the 2xx status. Fail loudly.
        var finalSize = new FileInfo(temp).Length;
        if (finalSize < 100_000)
        {
            try { File.Delete(temp); } catch { }
            throw new InvalidDataException(
                $"Downloaded file is only {finalSize:N0} bytes — the URL may not be pointing at a real traineddata file.");
        }

        if (File.Exists(destination)) File.Delete(destination);
        File.Move(temp, destination);
    }
}
