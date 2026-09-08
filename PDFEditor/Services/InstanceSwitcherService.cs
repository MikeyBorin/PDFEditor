using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PDFEditor.Services;

// Enumerates other running PDFEditor instances and activates one by HWND.
// Read on demand (menu open / Ctrl+Tab); no background timer, no state.
public static class InstanceSwitcherService
{
    public record OpenInstance(IntPtr Hwnd, int ProcessId, string FileLabel, string FullTitle);

    // Titles from MainViewModel.OnDocumentChanged are one of:
    //   "ArtiMax PDF Editor"                          (no file)
    //   "ArtiMax PDF Editor - foo.pdf"
    //   "ArtiMax PDF Editor - foo.pdf *"              (dirty)
    private const string TitlePrefix = "ArtiMax PDF Editor";

    public static List<OpenInstance> GetOtherInstances()
    {
        var result = new List<OpenInstance>();
        var selfPid = Environment.ProcessId;
        var selfName = Process.GetCurrentProcess().ProcessName;

        Process[] procs;
        try { procs = Process.GetProcessesByName(selfName); }
        catch { return result; }

        foreach (var p in procs)
        {
            try
            {
                if (p.Id == selfPid) continue;
                var hwnd = p.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;
                var title = GetWindowText(hwnd);
                if (string.IsNullOrEmpty(title)) continue;
                if (!title.StartsWith(TitlePrefix, StringComparison.Ordinal)) continue;

                var label = ExtractFileLabel(title);
                result.Add(new OpenInstance(hwnd, p.Id, label, title));
            }
            catch { /* process may have exited between enumerate and read */ }
            finally { p.Dispose(); }
        }

        result.Sort((a, b) => string.Compare(a.FileLabel, b.FileLabel, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static string ExtractFileLabel(string title)
    {
        // Strip prefix + " - ". If nothing left, the instance has no file open.
        var body = title.Length > TitlePrefix.Length ? title[TitlePrefix.Length..].TrimStart() : "";
        if (body.StartsWith("-")) body = body[1..].TrimStart();
        // Strip trailing " [N windows]" suffix that MainViewModel adds when
        // multiple instances are open, so the menu shows just the filename.
        var bracket = body.LastIndexOf('[');
        if (bracket > 0 && body.EndsWith("]"))
            body = body[..bracket].TrimEnd();
        if (body.Length == 0) return "(no file)";
        return body;
    }

    public static void Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // Restore if minimized, then bring to front.
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    // Sends WM_CLOSE via PostMessage — equivalent to clicking the window's X.
    // Each target instance runs its own Window_Closing handler, so the
    // unsaved-changes prompt fires per window.
    public static void RequestClose(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    // --- P/Invoke ---
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private static string GetWindowText(IntPtr hWnd)
    {
        var len = GetWindowTextLength(hWnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        var got = GetWindowText(hWnd, sb, sb.Capacity);
        return got > 0 ? sb.ToString() : "";
    }
}
