using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PDFEditor.Services;

/// <summary>
/// Registers PDF Editor as a per-user (HKCU) handler for .pdf so it appears in the
/// Windows "Open with" list and the Settings → Default apps picker. All writes are
/// under HKCU so no admin elevation is required. Idempotent — Register can be
/// called repeatedly without side effects.
/// </summary>
public class FileAssociationService
{
    private const string ProgId          = "ArtiMax.PDFDocument";
    private const string AppRegKey       = "ArtiMaxPDFEditor";
    private const string AppFriendlyName = "ArtiMax PDF Editor";
    private const string AppDescription  = "Free desktop PDF editor by ArtiMax (portable, MIT-licensed).";

    private static string? ExePath => Environment.ProcessPath;

    /// <summary>True if the ProgID + command are currently written.</summary>
    public bool IsRegistered()
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return k?.GetValue("") is string s && !string.IsNullOrEmpty(s);
    }

    public void Register()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe))
            throw new InvalidOperationException("Could not determine current executable path.");

        // ProgID — describes what "PDFEditor.PDFDocument" is.
        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey.SetValue("", "PDF Document");
            progIdKey.SetValue("FriendlyAppName", AppFriendlyName);
            using (var iconKey = progIdKey.CreateSubKey("DefaultIcon"))
                iconKey.SetValue("", $"\"{exe}\",0");
            using (var openKey = progIdKey.CreateSubKey(@"shell\open\command"))
                openKey.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // OpenWithProgids — makes the app appear in the "Open with" list for .pdf.
        using (var owKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\.pdf\OpenWithProgids"))
            owKey.SetValue(ProgId, "");

        // Capabilities — makes the app appear in Settings → Default apps.
        using (var caps = Registry.CurrentUser.CreateSubKey($@"Software\{AppRegKey}\Capabilities"))
        {
            caps.SetValue("ApplicationName", AppFriendlyName);
            caps.SetValue("ApplicationDescription", AppDescription);
            caps.SetValue("ApplicationIcon", $"\"{exe}\",0");
            using (var fa = caps.CreateSubKey("FileAssociations"))
                fa.SetValue(".pdf", ProgId);
        }

        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue(AppRegKey, $@"Software\{AppRegKey}\Capabilities");

        NotifyShell();
    }

    public void Unregister()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false); } catch { }

        using (var ow = Registry.CurrentUser.OpenSubKey($@"Software\Classes\.pdf\OpenWithProgids", writable: true))
            try { ow?.DeleteValue(ProgId, throwOnMissingValue: false); } catch { }

        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\{AppRegKey}", throwOnMissingSubKey: false); } catch { }
        using (var ra = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            try { ra?.DeleteValue(AppRegKey, throwOnMissingValue: false); } catch { }

        NotifyShell();
    }

    private static void NotifyShell()
    {
        // SHCNE_ASSOCCHANGED = 0x08000000 — tells Explorer to refresh its association cache.
        try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
