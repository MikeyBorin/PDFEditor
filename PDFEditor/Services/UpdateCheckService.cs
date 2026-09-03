using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace PDFEditor.Services;

/// <summary>Result of a check against the GitHub Releases API.</summary>
public record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    bool NewerAvailable,
    string? ReleasePageUrl,
    string? InstallerDownloadUrl,
    string? PortableDownloadUrl,
    string? PublishedAt,
    string? ErrorMessage);

/// <summary>
/// Manual-trigger update checker. Hits GitHub's public Releases API for the
/// project repo, parses the latest release, and compares against the current
/// assembly's InformationalVersion. Anonymous — no auth, no telemetry, no
/// callback: nothing runs unless the user picks Help → Check for Updates.
/// </summary>
public class UpdateCheckService
{
    // Repository path only; assembled into the API URL at call time so it's
    // easy to swap during a fork / rename without hunting through code.
    private const string RepoPath = "MikeyBorin/PDFEditor";

    public string RepoUrl        => $"https://github.com/{RepoPath}";
    public string LatestPageUrl  => $"{RepoUrl}/releases/latest";

    public string CurrentVersion => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+')[0]   // strip any "+commithash" build metadata
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "unknown";

    public async Task<UpdateCheckResult> CheckAsync()
    {
        var current = CurrentVersion;
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ArtiMaxPDFEditor-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{RepoPath}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag         = TryGetString(root, "tag_name") ?? "";
            var htmlUrl     = TryGetString(root, "html_url");
            var publishedAt = TryGetString(root, "published_at");

            string? installerUrl = null, portableUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = TryGetString(a, "name") ?? "";
                    var url  = TryGetString(a, "browser_download_url");
                    if (url == null) continue;
                    if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) installerUrl = url;
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) portableUrl = url;
                }
            }

            // Strip a leading 'v' so "v1.0.3" vs "1.0.3" compares cleanly.
            var latestNorm  = tag.TrimStart('v', 'V');
            var currentNorm = current.TrimStart('v', 'V');
            bool newer = TryParseVersion(latestNorm, out var lv)
                      && TryParseVersion(currentNorm, out var cv)
                      && lv > cv;

            return new UpdateCheckResult(
                CurrentVersion:       current,
                LatestVersion:        tag,
                NewerAvailable:       newer,
                ReleasePageUrl:       htmlUrl,
                InstallerDownloadUrl: installerUrl,
                PortableDownloadUrl:  portableUrl,
                PublishedAt:          publishedAt,
                ErrorMessage:         null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                CurrentVersion:       current,
                LatestVersion:        null,
                NewerAvailable:       false,
                ReleasePageUrl:       LatestPageUrl,
                InstallerDownloadUrl: null,
                PortableDownloadUrl:  null,
                PublishedAt:          null,
                ErrorMessage:         ex.Message);
        }
    }

    private static string? TryGetString(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryParseVersion(string s, out Version v)
    {
        // Accept "1", "1.0", "1.0.3", "1.0.3.0". Version.TryParse needs >=2 parts.
        var parts = (s ?? "").Split('.');
        if (parts.Length == 1) s = s + ".0";
        return Version.TryParse(s, out v!);
    }
}
